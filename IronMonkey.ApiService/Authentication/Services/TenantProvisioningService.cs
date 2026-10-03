using Npgsql;
using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Presentation;
using IronMonkey.Common;
using IronMonkey.Data.RecipeContent;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.ApiService.Authentication.Services;

public interface ITenantProvisioningService
{
    Task ProvisionTenantAsync(Guid signupRequestId, Guid? recipeId = null, CancellationToken cancellationToken = default);
}

public class TenantProvisioningService : ITenantProvisioningService
{
    private readonly CentralDbContext _centralDb;
    private readonly ITenantDbContextFactory _tenantContextFactory;
    private readonly IConfiguration _config;

    public TenantProvisioningService(
        CentralDbContext centralDb,
        ITenantDbContextFactory tenantContextFactory,
        IConfiguration config)
    {
        _centralDb = centralDb;
        _tenantContextFactory = tenantContextFactory;
        _config = config;
    }

    public async Task ProvisionTenantAsync(Guid signupRequestId, Guid? recipeId = null, CancellationToken cancellationToken = default)
    {
        // Step 1: Load and validate signup request
        var signupRequest = await _centralDb.SignupRequests
            .SingleOrDefaultAsync(r => r.Id == signupRequestId, cancellationToken)
            ?? throw new InvalidOperationException($"SignupRequest {signupRequestId} not found.");

        if (signupRequest.Status != "Approved")
            throw new InvalidOperationException("Can only provision Approved signup requests.");

        // Step 2: Create Tenant record in central DB
        var slug = GenerateSlug(signupRequest.CompanyName);
        var tenant = Tenant.Create(signupRequest.CompanyName, slug, "Standard", "Active");
        _centralDb.Tenants.Add(tenant);
        await _centralDb.SaveChangesAsync(cancellationToken);

        // Step 3: Create the PostgreSQL database
        var connectionString = await CreateTenantDatabaseAsync(slug, cancellationToken);

        // Step 4: Apply EF Core migrations to the new tenant DB
        await using var tenantDb = _tenantContextFactory.CreateForTenant(connectionString, tenant.Id);
        await tenantDb.Database.MigrateAsync(cancellationToken);

        // Step 4b: Validate recipe is active (D-12 per CONTEXT.md)
        var effectiveRecipeId = recipeId;
        if (effectiveRecipeId.HasValue)
        {
            var recipe = await _centralDb.IndustryRecipes
                .AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == effectiveRecipeId.Value, cancellationToken)
                ?? throw new InvalidOperationException($"Recipe {effectiveRecipeId.Value} not found.");

            if (!recipe.IsActive)
                throw new InvalidOperationException(
                    $"Cannot provision with deactivated recipe '{recipe.Name}'. Reactivate or select a different recipe.");
        }
        else
        {
            // Default to Blank recipe when no recipe selected (D-03)
            effectiveRecipeId = new Guid("00000000-0000-0000-0000-000000000001");
        }

        // Step 5: Seed default data
        await SeedTenantDataAsync(tenantDb, tenant, signupRequest, effectiveRecipeId, cancellationToken);

        // Step 6: Mark tenant as provisioned in central DB
        tenant.MarkProvisioned(connectionString);
        signupRequest.LinkTenant(tenant.Id);

        // Step 6a: Track applied recipe in central DB (D-11)
        if (effectiveRecipeId.HasValue)
        {
            var appliedRecipe = await _centralDb.IndustryRecipes
                .AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == effectiveRecipeId.Value, cancellationToken);
            if (appliedRecipe != null)
                tenant.SetAppliedRecipe(appliedRecipe.Id, appliedRecipe.Version);
        }

        await _centralDb.SaveChangesAsync(cancellationToken);

        // Step 7: Register admin email in central user-tenant index for login resolution
        _centralDb.UserTenantIndex.Add(UserTenantIndex.Create(signupRequest.AdminEmail, tenant.Id));
        await _centralDb.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> CreateTenantDatabaseAsync(string slug, CancellationToken cancellationToken)
    {
        var centralConnStr = _config.GetConnectionString("CentralDb")
            ?? throw new InvalidOperationException("CentralDb connection string required.");

        var dbName = $"ironmonkey_{slug.ToLower().Replace("-", "_")}";

        await using var connection = new NpgsqlConnection(centralConnStr);
        await connection.OpenAsync(cancellationToken);

        // Check if DB already exists to make provisioning idempotent
        await using var checkCmd = new NpgsqlCommand(
            $"SELECT 1 FROM pg_database WHERE datname = '{dbName}'", connection);
        var exists = await checkCmd.ExecuteScalarAsync(cancellationToken);

        if (exists is null)
        {
            await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", connection);
            await createCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Build connection string for new tenant DB using same server/credentials as central
        var builder = new NpgsqlConnectionStringBuilder(centralConnStr) { Database = dbName };
        return builder.ToString();
    }

    private async Task SeedTenantDataAsync(
        TenantDbContext db,
        Tenant tenant,
        SignupRequest signupRequest,
        Guid? recipeId,
        CancellationToken cancellationToken)
    {
        // Roles are already seeded by EF migration (SuperAdmin=1, Admin=201, Owner=301, TeleCaller=302)
        var adminRole = await db.Roles.SingleAsync(r => r.Name == "Admin", cancellationToken);

        // Load recipe if provided (D-06)
        RecipeContentModel? content = null;
        if (recipeId.HasValue)
        {
            var recipe = await _centralDb.IndustryRecipes
                .AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == recipeId.Value, cancellationToken);

            if (recipe == null)
                throw new InvalidOperationException($"Recipe {recipeId} not found.");

            content = System.Text.Json.JsonSerializer.Deserialize<RecipeContentModel>(recipe.ContentJson)
                ?? new RecipeContentModel();
        }

        // Copy the vertical's vocabulary and formatting onto the tenant row. Like every other
        // recipe artifact this is a copy, not a link: the tenant may rename any of it
        // afterwards without touching the platform catalog. Recipes stored before recipe
        // presentation existed carry null here and leave the tenant on built-in defaults.
        if (content?.Presentation is { } presentation &&
            (presentation.Terminology is not null || presentation.Locale is not null))
        {
            tenant.UpdatePresentation(new TenantPresentationSettings
            {
                Terminology = presentation.Terminology,
                Locale = presentation.Locale
            });
        }

        // ── Pipelines and their stages (D-07: stages before fields and rules) ──────
        //
        // Every stage a tenant is provisioned with belongs to a pipeline from the moment it
        // is created, so a freshly provisioned tenant is never in the orphaned-stage state
        // the migration exists to clear up for older ones.
        //
        // A recipe that names pipelines is authoritative. One that does not — which is every
        // recipe stored before this existed — has its flat stage lists read as the default
        // lead and opportunity pipelines, which is exactly what they meant before pipelines
        // existed. That is what keeps an old recipe provisioning to a single-pipeline tenant.
        var pipelineDefs = BuildPipelineDefinitions(content);

        foreach (var pipelineDef in pipelineDefs)
        {
            var recordType = Enum.TryParse<PipelineRecordType>(pipelineDef.RecordType, ignoreCase: true, out var parsedType)
                ? parsedType
                : PipelineRecordType.Lead;

            var pipeline = Pipeline.Create(
                tenant.Id, recordType, pipelineDef.Name, pipelineDef.Order,
                pipelineDef.IsDefault, pipelineDef.Description);

            db.Pipelines.Add(pipeline);

            foreach (var stageDef in pipelineDef.Stages)
            {
                var stageType = Enum.TryParse<StageType>(stageDef.StageType, out var parsed)
                    ? parsed
                    : StageType.Active;

                // The recipe's Order is used VERBATIM. Some stored recipes number their
                // stages from 0 and some from 1, and both are correct — Order only has to be
                // a consistent sort key within the pipeline. Substituting a 1-based sequence
                // for a 0-based one would silently renumber every stage of those recipes and
                // break the tests that pin their exact seeded shape.
                //
                // CreateIn, not CreateFor: the stage takes its record type from the pipeline
                // it is placed in, so the two can never disagree.
                db.PipelineStages.Add(PipelineStage.CreateIn(
                    pipeline, stageDef.Name, stageDef.Order, stageType));
            }
        }

        // Apply custom field definitions (D-07: fields after stages)
        var fields = content?.CustomFields ?? [];
        foreach (var fieldDef in fields)
        {
            var fieldType = Enum.TryParse<CustomFieldType>(fieldDef.FieldType, out var parsedType)
                ? parsedType
                : CustomFieldType.Text;
            var field = CustomFieldDefinition.Create(tenant.Id, fieldDef.FieldName, fieldType, fieldDef.IsRequired, fieldDef.Options);
            db.CustomFieldDefinitions.Add(field);
        }

        // Apply workflow rules (D-07: rules after fields)
        var rules = content?.WorkflowRules ?? [];
        foreach (var ruleDef in rules)
        {
            var trigger = Enum.TryParse<WorkflowTrigger>(ruleDef.Trigger, out var parsedTrigger)
                ? parsedTrigger
                : WorkflowTrigger.FieldChange;
            var rule = WorkflowRule.Create(tenant.Id, ruleDef.Name, trigger, ruleDef.ConditionJson, ruleDef.ActionJson);
            db.WorkflowRules.Add(rule);
        }

        // Flush stages, fields, and rules to DB so stageMap can resolve stage IDs
        await db.SaveChangesAsync(cancellationToken);

        // Seed sample leads from recipe (D-18: after stages are flushed to DB)
        var sampleLeads = content?.SampleLeads ?? [];
        if (sampleLeads.Count > 0)
        {
            // Lead stages only: opportunity stages now live in the same table and a sample
            // lead placed in one would be invisible on every lead board.
            var stageMap = await db.PipelineStages
                .AsNoTracking()
                .Where(s => s.TenantId == tenant.Id && s.RecordType == PipelineRecordType.Lead)
                .ToDictionaryAsync(s => s.Name, s => s.Id, cancellationToken);

            foreach (var leadDef in sampleLeads)
            {
                if (!stageMap.TryGetValue(leadDef.StageName, out var stageId))
                    continue; // skip silently if stage name doesn't match

                var source = Enum.TryParse<LeadSource>(leadDef.Source, out var parsedSource)
                    ? parsedSource
                    : LeadSource.WebForm;

                var lead = Lead.Create(tenant.Id, leadDef.FirstName, leadDef.LastName,
                    leadDef.Mobile, leadDef.Email, source, stageId);

                foreach (var kvp in leadDef.CustomFieldValues)
                {
                    lead.CustomFields.Set(kvp.Key, kvp.Value);
                }

                db.Leads.Add(lead);
            }
        }

        // Seed admin user and leads with stage IDs resolved from DB
        var adminUser = User.Create(
            tenant.Id,
            signupRequest.AdminEmail.Split('@')[0],
            signupRequest.AdminEmail,
            signupRequest.AdminPasswordHash,
            adminRole);
        db.Users.Add(adminUser);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Turns a recipe into the list of pipelines to seed, applying the backward-compatibility
    /// rules in one place so provisioning itself stays a straight loop.
    ///
    /// <para>Three cases:</para>
    /// <list type="number">
    /// <item><b>The recipe names pipelines.</b> Used as given. The flat
    ///   <c>PipelineStages</c>/<c>OpportunityStages</c> lists are ignored — a recipe that
    ///   specified both would otherwise produce a surprising merge.</item>
    /// <item><b>The recipe names none (null or empty).</b> Its flat lead stage list becomes
    ///   the default lead pipeline and its opportunity stage list the default opportunity
    ///   one. This is every recipe stored before pipelines existed, and it provisions to
    ///   exactly the single-pipeline tenant it always did.</item>
    /// <item><b>No recipe at all.</b> Product defaults for both record types.</item>
    /// </list>
    ///
    /// <para>
    /// Exactly one pipeline per record type is marked default. If a recipe marks none — or,
    /// wrongly, marks several — the first of that record type wins and the rest are cleared,
    /// because the filtered unique index permits exactly one and a record type with none has
    /// no answer for "no pipeline specified".
    /// </para>
    /// </summary>
    internal static List<PipelineDefinition> BuildPipelineDefinitions(RecipeContentModel? content)
    {
        List<PipelineDefinition> definitions;

        if (content?.Pipelines is { Count: > 0 } named)
        {
            definitions = [.. named.Select(p => new PipelineDefinition
            {
                Name = string.IsNullOrWhiteSpace(p.Name) ? "Default" : p.Name.Trim(),
                RecordType = p.RecordType,
                Description = p.Description,
                IsDefault = p.IsDefault,
                Order = p.Order,
                Stages = p.Stages.Count > 0
                    ? p.Stages
                    : DefaultStagesFor(p.RecordType)
            })];
        }
        else
        {
            definitions = [];

            // The flat lead list. Empty (or absent) still produces a pipeline — a tenant with
            // no lead pipeline could not hold a lead at all, which no recipe means to express.
            var leadStages = content?.PipelineStages is { Count: > 0 } ls
                ? ls
                : DefaultStagesFor(nameof(PipelineRecordType.Lead));

            definitions.Add(new PipelineDefinition
            {
                Name = TenantDbContext.DefaultLeadPipelineName,
                RecordType = nameof(PipelineRecordType.Lead),
                IsDefault = true,
                Order = 1,
                Stages = leadStages
            });

            // Opportunity stages keep Part A's null-vs-empty distinction exactly: null means
            // "no opinion, use the product defaults", while an explicitly empty list means
            // "seed none" and is honoured — the pipeline is still created (so the tenant can
            // add stages to it later) but arrives with no stages, as authored.
            var opportunityStages = content?.OpportunityStages
                ?? DefaultStagesFor(nameof(PipelineRecordType.Opportunity));

            definitions.Add(new PipelineDefinition
            {
                Name = TenantDbContext.DefaultOpportunityPipelineName,
                RecordType = nameof(PipelineRecordType.Opportunity),
                IsDefault = true,
                Order = 1,
                Stages = opportunityStages
            });
        }

        // Exactly one default per record type.
        foreach (var group in definitions.GroupBy(d =>
                     Enum.TryParse<PipelineRecordType>(d.RecordType, ignoreCase: true, out var t)
                         ? t : PipelineRecordType.Lead))
        {
            var ordered = group.OrderBy(d => d.Order).ThenBy(d => d.Name, StringComparer.Ordinal).ToList();
            var chosen = ordered.FirstOrDefault(d => d.IsDefault) ?? ordered[0];

            foreach (var d in ordered) d.IsDefault = ReferenceEquals(d, chosen);
        }

        return definitions;
    }

    /// <summary>The product's default stage set for a record type, shared by every path that
    /// needs one so the three cannot drift.</summary>
    private static List<PipelineStageDefinition> DefaultStagesFor(string recordType) =>
        string.Equals(recordType, nameof(PipelineRecordType.Opportunity), StringComparison.OrdinalIgnoreCase)
            ? [.. OpportunityStages.Defaults.Select(d =>
                new PipelineStageDefinition { Name = d.Name, Order = d.Order, StageType = d.StageType })]
            :
            [
                new() { Name = "New", Order = 1, StageType = nameof(StageType.Entry) },
                new() { Name = "Contacted", Order = 2, StageType = nameof(StageType.Active) },
                new() { Name = "Qualified", Order = 3, StageType = nameof(StageType.Active) },
                new() { Name = "Converted", Order = 4, StageType = nameof(StageType.ClosedWon) },
                new() { Name = "Closed Lost", Order = 5, StageType = nameof(StageType.ClosedLost) }
            ];

    private static string GenerateSlug(string companyName)
    {
        return companyName
            .ToLower()
            .Replace(" ", "-")
            .Replace("_", "-")
            .Trim('-');
    }
}
