using Npgsql;
using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
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

        // Apply pipeline stages (D-07: stages before fields and rules)
        var stages = content?.PipelineStages ?? [];
        foreach (var stageDef in stages)
        {
            var stageType = Enum.TryParse<StageType>(stageDef.StageType, out var parsed)
                ? parsed
                : StageType.Active;
            var stage = PipelineStage.Create(tenant.Id, stageDef.Name, stageDef.Order, stageType);
            db.PipelineStages.Add(stage);
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
            var stageMap = await db.PipelineStages
                .AsNoTracking()
                .Where(s => s.TenantId == tenant.Id)
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

    private static string GenerateSlug(string companyName)
    {
        return companyName
            .ToLower()
            .Replace(" ", "-")
            .Replace("_", "-")
            .Trim('-');
    }
}
