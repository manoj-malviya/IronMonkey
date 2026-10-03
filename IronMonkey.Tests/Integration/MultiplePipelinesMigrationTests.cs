using IronMonkey.Data;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// Covers the <c>MultiplePipelines</c> data migration.
///
/// The requirement it has to satisfy is absolute: <b>every existing stage must end up
/// attached to a default pipeline — no tenant may be left with stages belonging to no
/// pipeline.</b> That failure is silent in a naive implementation. An orphaned stage does not
/// throw; it simply never appears in a picker or on a board, and the records sitting in it
/// become unreachable.
///
/// So these tests migrate to the *previous* migration, seed rows through raw SQL in the
/// pre-migration schema, apply the migration under test, and then assert on the resulting
/// data rather than merely on the migration completing.
/// </summary>
[Collection("Integration")]
public class MultiplePipelinesMigrationTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    /// <summary>The migration immediately before the one under test — Part A's.</summary>
    private const string PreviousMigration = "20260920144908_OpportunityStages";

    private readonly TenantDbContextFactory _factory = new();

    private async Task<string> MigrateToPreviousAsync(Guid tenantId, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connectionString = fixture.ConnectionString.Replace("ironmonkey_test", $"pipemig_{label}_{suffix}");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        return connectionString;
    }

    private async Task ApplyMigrationAsync(string connectionString, Guid tenantId)
    {
        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// Inserts a stage in the pre-migration schema, which has RecordType but no PipelineId.
    /// </summary>
    private static async Task SeedStageAsync(
        NpgsqlConnection connection, Guid tenantId, Guid stageId,
        string name, int order, string recordType, string stageType = "Active")
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO pipeline_stages
                (""Id"", ""TenantId"", ""Name"", ""Order"", ""IsActive"", ""StageType"",
                 ""RecordType"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
            VALUES (@id, @tenant, @name, @order, true, @stageType, @recordType, now(), now(), false);";

        command.Parameters.AddWithValue("id", stageId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("order", order);
        command.Parameters.AddWithValue("stageType", stageType);
        command.Parameters.AddWithValue("recordType", recordType);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedLeadAsync(
        NpgsqlConnection connection, Guid tenantId, Guid leadId, Guid stageId, string email)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO leads
                (""Id"", ""TenantId"", ""FirstName"", ""LastName"", ""Mobile"", ""Email"", ""Source"",
                 ""PipelineStageId"", ""IsConverted"", ""IsPotentialDuplicate"",
                 ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"", custom_field_values)
            VALUES (@id, @tenant, 'Legacy', 'Lead', '555', @email, 'Manual',
                    @stage, false, false, now(), now(), false, '{}'::jsonb);";

        command.Parameters.AddWithValue("id", leadId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("stage", stageId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedOpportunityAsync(
        NpgsqlConnection connection, Guid tenantId, Guid contactId, Guid opportunityId, Guid stageId)
    {
        await using (var contact = connection.CreateCommand())
        {
            contact.CommandText = @"
                INSERT INTO contacts (""Id"", ""TenantId"", ""Name"", ""Mobile"", ""Email"",
                                      ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"", custom_field_values)
                VALUES (@id, @tenant, 'Legacy Contact', '555', @email, now(), now(), false, '{}'::jsonb);";
            contact.Parameters.AddWithValue("id", contactId);
            contact.Parameters.AddWithValue("tenant", tenantId);
            contact.Parameters.AddWithValue("email", $"c{contactId:N}@t.com");
            await contact.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO opportunities
                (""Id"", ""TenantId"", ""Title"", ""ContactId"", ""ExpectedCloseDate"",
                 ""PipelineStageId"", ""Amount"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
            VALUES (@id, @tenant, 'Legacy Deal', @contact, now(), @stage, 1000, now(), now(), false);";

        command.Parameters.AddWithValue("id", opportunityId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("contact", contactId);
        command.Parameters.AddWithValue("stage", stageId);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Every_existing_stage_is_attached_to_a_default_pipeline()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await MigrateToPreviousAsync(tenantId, "stages");

        var leadStage = Guid.NewGuid();
        var oppStage = Guid.NewGuid();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await SeedStageAsync(connection, tenantId, leadStage, "New", 1, "Lead", "Entry");
            await SeedStageAsync(connection, tenantId, oppStage, "Qualification", 1, "Opportunity", "Entry");
        }

        await ApplyMigrationAsync(connectionString, tenantId);

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        // Not a single stage may be left holding the placeholder. The migration's own guard
        // would have aborted, so reaching here at all is part of the assertion — but the
        // count is checked explicitly so a future change that removes the guard still fails.
        var orphaned = await db.PipelineStages
            .CountAsync(s => s.PipelineId == Guid.Empty);

        Assert.Equal(0, orphaned);

        var pipelines = await db.Pipelines.ToListAsync();

        // One per record type that had stages, each the default for its type.
        Assert.Equal(2, pipelines.Count);
        Assert.All(pipelines, p => Assert.True(p.IsDefault));

        var lead = await db.PipelineStages.SingleAsync(s => s.Id == leadStage);
        var opp = await db.PipelineStages.SingleAsync(s => s.Id == oppStage);

        // Each stage joined the pipeline OF ITS OWN RECORD TYPE. Matching on tenant alone
        // would have filed the deal stage into the lead pipeline, which is exactly the
        // cross-pipeline corruption the feature exists to prevent.
        var leadPipeline = pipelines.Single(p => p.RecordType == Data.Entities.PipelineRecordType.Lead);
        var oppPipeline = pipelines.Single(p => p.RecordType == Data.Entities.PipelineRecordType.Opportunity);

        Assert.Equal(leadPipeline.Id, lead.PipelineId);
        Assert.Equal(oppPipeline.Id, opp.PipelineId);
    }

    [Fact]
    public async Task Records_take_the_pipeline_of_the_stage_they_are_already_in()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await MigrateToPreviousAsync(tenantId, "records");

        var leadStage = Guid.NewGuid();
        var oppStage = Guid.NewGuid();
        var leadId = Guid.NewGuid();
        var opportunityId = Guid.NewGuid();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await SeedStageAsync(connection, tenantId, leadStage, "New", 1, "Lead", "Entry");
            await SeedStageAsync(connection, tenantId, oppStage, "Qualification", 1, "Opportunity", "Entry");
            await SeedLeadAsync(connection, tenantId, leadId, leadStage, "legacy@t.com");
            await SeedOpportunityAsync(connection, tenantId, Guid.NewGuid(), opportunityId, oppStage);
        }

        await ApplyMigrationAsync(connectionString, tenantId);

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var lead = await db.Leads.SingleAsync(l => l.Id == leadId);
        var opportunity = await db.Opportunities.SingleAsync(o => o.Id == opportunityId);
        var stageOfLead = await db.PipelineStages.SingleAsync(s => s.Id == leadStage);
        var stageOfOpportunity = await db.PipelineStages.SingleAsync(s => s.Id == oppStage);

        // A record's pipeline IS the pipeline of the stage it sits in. Anything else would be
        // a record pointing at a stage from a pipeline it is not in.
        Assert.Equal(stageOfLead.PipelineId, lead.PipelineId);
        Assert.Equal(stageOfOpportunity.PipelineId, opportunity.PipelineId);
        Assert.NotEqual(Guid.Empty, lead.PipelineId);
        Assert.NotEqual(lead.PipelineId, opportunity.PipelineId);
    }

    [Fact]
    public async Task Several_tenants_in_one_database_each_get_their_own_pipelines()
    {
        // The pipeline rows are derived from the stage rows' own TenantId rather than
        // assumed. Without that, one tenant's stages could be filed into another's pipeline —
        // a cross-tenant leak, not merely a cross-pipeline one.
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var connectionString = await MigrateToPreviousAsync(tenantA, "multitenant");

        var stageA = Guid.NewGuid();
        var stageB = Guid.NewGuid();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await SeedStageAsync(connection, tenantA, stageA, "New", 1, "Lead", "Entry");
            await SeedStageAsync(connection, tenantB, stageB, "New", 1, "Lead", "Entry");
        }

        await ApplyMigrationAsync(connectionString, tenantA);

        await using var dbA = _factory.CreateForTenant(connectionString, tenantA);
        await using var dbB = _factory.CreateForTenant(connectionString, tenantB);

        var pipelineA = await dbA.Pipelines.SingleAsync();
        var pipelineB = await dbB.Pipelines.SingleAsync();

        Assert.NotEqual(pipelineA.Id, pipelineB.Id);
        Assert.Equal(tenantA, pipelineA.TenantId);
        Assert.Equal(tenantB, pipelineB.TenantId);

        var resolvedA = await dbA.PipelineStages.SingleAsync(s => s.Id == stageA);
        var resolvedB = await dbB.PipelineStages.SingleAsync(s => s.Id == stageB);

        Assert.Equal(pipelineA.Id, resolvedA.PipelineId);
        Assert.Equal(pipelineB.Id, resolvedB.PipelineId);
    }

    [Fact]
    public async Task Existing_history_is_attributed_to_the_pipeline_it_happened_in()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await MigrateToPreviousAsync(tenantId, "history");

        var stageOne = Guid.NewGuid();
        var stageTwo = Guid.NewGuid();
        var leadId = Guid.NewGuid();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await SeedStageAsync(connection, tenantId, stageOne, "New", 1, "Lead", "Entry");
            await SeedStageAsync(connection, tenantId, stageTwo, "Contacted", 2, "Lead");
            await SeedLeadAsync(connection, tenantId, leadId, stageTwo, "hist@t.com");

            await using var change = connection.CreateCommand();
            change.CommandText = @"
                INSERT INTO stage_changes
                    (""Id"", ""TenantId"", ""RecordType"", ""RecordId"", ""FromStageId"", ""ToStageId"",
                     ""OccurredAt"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES (@id, @tenant, 'Lead', @record, @from, @to, now(), now(), now(), false);";
            change.Parameters.AddWithValue("id", Guid.NewGuid());
            change.Parameters.AddWithValue("tenant", tenantId);
            change.Parameters.AddWithValue("record", leadId);
            change.Parameters.AddWithValue("from", stageOne);
            change.Parameters.AddWithValue("to", stageTwo);
            await change.ExecuteNonQueryAsync();
        }

        await ApplyMigrationAsync(connectionString, tenantId);

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var pipeline = await db.Pipelines.SingleAsync();
        var recorded = await db.StageChanges.SingleAsync(c => c.RecordId == leadId);

        // Left at the placeholder, this row would be silently excluded from every
        // per-pipeline velocity report — the history would appear to start at migration time.
        Assert.Equal(pipeline.Id, recorded.ToPipelineId);
        Assert.Equal(pipeline.Id, recorded.FromPipelineId);

        // And it is NOT reported as a cross-pipeline move: no such move can have happened
        // before pipelines existed.
        Assert.False(recorded.IsPipelineChange);
    }

    [Fact]
    public async Task Rules_fields_and_routing_stay_tenant_wide_after_the_migration()
    {
        // NULL is the correct value for every pre-existing row and means "tenant-wide",
        // which is what these rows meant before pipelines existed. Defaulting them to the
        // default pipeline would have silently narrowed every one of them the day a tenant
        // created a second pipeline.
        var tenantId = Guid.NewGuid();
        var connectionString = await MigrateToPreviousAsync(tenantId, "targeting");

        var stageId = Guid.NewGuid();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await SeedStageAsync(connection, tenantId, stageId, "New", 1, "Lead", "Entry");

            await using var rule = connection.CreateCommand();
            rule.CommandText = @"
                INSERT INTO workflow_rules
                    (""Id"", ""TenantId"", ""Name"", ""Trigger"", ""ConditionJson"", ""ActionJson"",
                     ""IsActive"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES (@id, @tenant, 'Legacy rule', 'StatusChange', '{}'::jsonb, '{}'::jsonb,
                        true, now(), now(), false);";
            rule.Parameters.AddWithValue("id", Guid.NewGuid());
            rule.Parameters.AddWithValue("tenant", tenantId);
            await rule.ExecuteNonQueryAsync();
        }

        await ApplyMigrationAsync(connectionString, tenantId);

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var migrated = await db.WorkflowRules.SingleAsync();
        Assert.Null(migrated.PipelineId);
    }
}
