using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// Covers the data migration from the free-text <c>opportunities.Stage</c> column to a
/// tenant-configured stage row.
///
/// The column was free text, so production data can hold anything, and the failure this
/// guards against is silent: a row that fails to map does not throw at migration time in a
/// naive implementation — it ends up pointing at a stage that does not exist, and the
/// opportunity simply stops appearing on every board and report. So each class of legacy
/// value is asserted on explicitly rather than just checking that the migration completed.
///
/// These tests migrate to the *previous* migration, insert legacy rows with raw SQL (the
/// entity no longer has a Stage property to write through), and then apply the migration
/// under test.
/// </summary>
[Collection("Integration")]
public class OpportunityStageMigrationTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private const string PreviousMigration = "20260920131437_TeamInvitations";

    private readonly TenantDbContextFactory _factory = new();

    private async Task<string> MigrateToPreviousAsync(Guid tenantId, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connectionString = fixture.ConnectionString.Replace("ironmonkey_test", $"oppmig_{label}_{suffix}");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        return connectionString;
    }

    /// <summary>
    /// Inserts a contact and an opportunity through raw SQL, in the pre-migration schema.
    /// </summary>
    private static async Task SeedLegacyOpportunityAsync(
        string connectionString, Guid tenantId, Guid contactId, Guid opportunityId,
        string title, string? legacyStage, decimal amount = 100m)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var contact = connection.CreateCommand())
        {
            contact.CommandText = @"
                INSERT INTO contacts (""Id"", ""TenantId"", ""Name"", ""Mobile"", ""Email"",
                                      ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"", custom_field_values)
                VALUES (@id, @tenant, 'Legacy Contact', '555', @email, now(), now(), false, '{}'::jsonb)
                ON CONFLICT DO NOTHING;";
            contact.Parameters.AddWithValue("id", contactId);
            contact.Parameters.AddWithValue("tenant", tenantId);
            contact.Parameters.AddWithValue("email", $"c{contactId:N}@legacy.test");
            await contact.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO opportunities (""Id"", ""TenantId"", ""Title"", ""ContactId"", ""Stage"",
                                       ""Amount"", ""ExpectedCloseDate"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
            VALUES (@id, @tenant, @title, @contact, @stage, @amount, now(), now(), now(), false);";
        command.Parameters.AddWithValue("id", opportunityId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("contact", contactId);
        command.Parameters.AddWithValue("stage", (object?)legacyStage ?? DBNull.Value);
        command.Parameters.AddWithValue("amount", amount);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ApplyMigrationAsync(string connectionString, Guid tenantId)
    {
        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        await db.Database.MigrateAsync();
    }

    [Fact]
    public async Task Every_default_stage_name_maps_to_the_seeded_stage_of_the_same_type()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await MigrateToPreviousAsync(tenantId, "defaults");
        var contactId = Guid.NewGuid();

        var expected = new[]
        {
            ("Qualification", StageType.Entry),
            ("Proposal", StageType.Active),
            ("Negotiation", StageType.Active),
            ("Won", StageType.ClosedWon),
            ("Lost", StageType.ClosedLost)
        };

        var ids = new Dictionary<string, Guid>();
        foreach (var (name, _) in expected)
        {
            var id = Guid.NewGuid();
            ids[name] = id;
            await SeedLegacyOpportunityAsync(connectionString, tenantId, contactId, id, $"Deal {name}", name);
        }

        await ApplyMigrationAsync(connectionString, tenantId);

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        foreach (var (name, stageType) in expected)
        {
            var opportunity = await db.Opportunities
                .Include(o => o.Stage)
                .SingleAsync(o => o.Id == ids[name]);

            Assert.Equal(name, opportunity.Stage.Name);
            Assert.Equal(stageType, opportunity.Stage.StageType);
            Assert.Equal(PipelineRecordType.Opportunity, opportunity.Stage.RecordType);
        }
    }

    /// <summary>
    /// The literal MarkAsLost() wrote. It has to land on a ClosedLost stage, not merely on
    /// a stage that happens to be called "Lost" — terminal reporting reads the type.
    /// </summary>
    [Fact]
    public async Task The_literal_Lost_written_by_MarkAsLost_lands_on_a_ClosedLost_stage()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await MigrateToPreviousAsync(tenantId, "lost");
        var contactId = Guid.NewGuid();
        var opportunityId = Guid.NewGuid();

        await SeedLegacyOpportunityAsync(
            connectionString, tenantId, contactId, opportunityId, "Lost deal", "Lost");

        await ApplyMigrationAsync(connectionString, tenantId);

        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        var opportunity = await db.Opportunities.Include(o => o.Stage).SingleAsync(o => o.Id == opportunityId);

        Assert.Equal(StageType.ClosedLost, opportunity.Stage.StageType);
        Assert.True(opportunity.Stage.IsTerminal);
    }

    [Theory]
    [InlineData("won")]          // wrong case
    [InlineData("  Proposal ")]  // padded
    public async Task Case_and_whitespace_variants_still_map(string legacyStage)
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await MigrateToPreviousAsync(tenantId, "variant");
        var contactId = Guid.NewGuid();
        var opportunityId = Guid.NewGuid();

        await SeedLegacyOpportunityAsync(
            connectionString, tenantId, contactId, opportunityId, "Variant deal", legacyStage);

        await ApplyMigrationAsync(connectionString, tenantId);

        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        var opportunity = await db.Opportunities.Include(o => o.Stage).SingleAsync(o => o.Id == opportunityId);

        Assert.Equal(legacyStage.Trim(), opportunity.Stage.Name, ignoreCase: true);
        Assert.NotEqual("Unsorted (migrated)", opportunity.Stage.Name);
    }

    /// <summary>
    /// The case the free-text column makes inevitable. An unrecognised value must not drop
    /// the row and must not leave it pointing at nothing — it goes to a holding stage that
    /// is explicitly NOT terminal, because an unknown deal is not a closed deal.
    /// </summary>
    /// <remarks>
    /// NULL is deliberately not a case here: the old column was declared NOT NULL, so no row
    /// could ever hold one. (Asserting it originally failed at the *seeding* step, which is
    /// how that was established.) The migration's mapping still handles NULL defensively —
    /// `lower(btrim(NULL)) = ...` is NULL, so such a row would fall through to the holding
    /// stage rather than being missed — but the case is unreachable, so it is not asserted.
    /// </remarks>
    [Theory]
    [InlineData("Discovery")]      // a value from a build that predates the current list
    [InlineData("")]               // empty string
    [InlineData("   ")]            // whitespace only
    public async Task Unmatched_values_land_in_a_non_terminal_holding_stage(string legacyStage)
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await MigrateToPreviousAsync(tenantId, "unmatched");
        var contactId = Guid.NewGuid();
        var opportunityId = Guid.NewGuid();

        await SeedLegacyOpportunityAsync(
            connectionString, tenantId, contactId, opportunityId, "Odd deal", legacyStage, amount: 4242m);

        await ApplyMigrationAsync(connectionString, tenantId);

        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        var opportunity = await db.Opportunities.Include(o => o.Stage).SingleAsync(o => o.Id == opportunityId);

        // Not dropped, and its data survived intact.
        Assert.Equal("Odd deal", opportunity.Title);
        Assert.Equal(4242m, opportunity.Amount);

        Assert.Equal("Unsorted (migrated)", opportunity.Stage.Name);

        // The important half: an unknown stage must never be counted as won or lost.
        Assert.False(opportunity.Stage.IsTerminal);
        Assert.Equal(StageType.Active, opportunity.Stage.StageType);
        Assert.True(opportunity.Stage.IsActive);
    }

    /// <summary>
    /// A tenant whose data is entirely recognisable should not be given a holding stage it
    /// has no use for — it would show up in every picker as clutter.
    /// </summary>
    [Fact]
    public async Task No_holding_stage_is_created_when_every_value_matched()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await MigrateToPreviousAsync(tenantId, "clean");
        var contactId = Guid.NewGuid();

        await SeedLegacyOpportunityAsync(
            connectionString, tenantId, contactId, Guid.NewGuid(), "Clean deal", "Proposal");

        await ApplyMigrationAsync(connectionString, tenantId);

        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        var stageNames = await db.PipelineStages
            .Where(s => s.RecordType == PipelineRecordType.Opportunity)
            .Select(s => s.Name)
            .ToListAsync();

        Assert.DoesNotContain("Unsorted (migrated)", stageNames);
        Assert.Equal(5, stageNames.Count);
    }

    [Fact]
    public async Task No_opportunity_is_left_pointing_at_a_nonexistent_stage()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await MigrateToPreviousAsync(tenantId, "orphan");
        var contactId = Guid.NewGuid();

        foreach (var legacy in new[] { "Won", "Lost", "Discovery", "proposal", "" })
        {
            await SeedLegacyOpportunityAsync(
                connectionString, tenantId, contactId, Guid.NewGuid(), $"Deal {legacy}", legacy);
        }

        await ApplyMigrationAsync(connectionString, tenantId);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT count(*) FROM opportunities o
            WHERE NOT EXISTS (SELECT 1 FROM pipeline_stages p WHERE p.""Id"" = o.""PipelineStageId"");";

        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    /// <summary>
    /// Existing stages are lead stages, and must stay that way — reclassifying them would
    /// empty every lead board in the tenant.
    /// </summary>
    [Fact]
    public async Task Existing_stages_are_classified_as_lead_stages()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await MigrateToPreviousAsync(tenantId, "leadstage");

        var leadStageId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO pipeline_stages (""Id"", ""TenantId"", ""Name"", ""Order"", ""IsActive"",
                                             ""StageType"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES (@id, @tenant, 'New', 1, true, 'Entry', now(), now(), false);";
            command.Parameters.AddWithValue("id", leadStageId);
            command.Parameters.AddWithValue("tenant", tenantId);
            await command.ExecuteNonQueryAsync();
        }

        await ApplyMigrationAsync(connectionString, tenantId);

        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        var stage = await db.PipelineStages.SingleAsync(s => s.Id == leadStageId);

        Assert.Equal(PipelineRecordType.Lead, stage.RecordType);
        Assert.Equal("New", stage.Name);
    }

    /// <summary>
    /// Each tenant gets its own stage rows. Sharing them would make one tenant's rename
    /// visible to another, and a reorder in one pipeline would reorder every tenant's.
    /// </summary>
    [Fact]
    public async Task Stages_are_seeded_per_tenant_not_shared()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var connectionString = await MigrateToPreviousAsync(tenantA, "multi");

        await SeedLegacyOpportunityAsync(
            connectionString, tenantA, Guid.NewGuid(), Guid.NewGuid(), "A deal", "Won");
        await SeedLegacyOpportunityAsync(
            connectionString, tenantB, Guid.NewGuid(), Guid.NewGuid(), "B deal", "Won");

        await ApplyMigrationAsync(connectionString, tenantA);

        await using var dbA = _factory.CreateForTenant(connectionString, tenantA);
        await using var dbB = _factory.CreateForTenant(connectionString, tenantB);

        var stagesA = await dbA.PipelineStages
            .Where(s => s.RecordType == PipelineRecordType.Opportunity).ToListAsync();
        var stagesB = await dbB.PipelineStages
            .Where(s => s.RecordType == PipelineRecordType.Opportunity).ToListAsync();

        Assert.Equal(5, stagesA.Count);
        Assert.Equal(5, stagesB.Count);
        Assert.Empty(stagesA.Select(s => s.Id).Intersect(stagesB.Select(s => s.Id)));
    }

    /// <summary>
    /// The migration writes an opening-balance history row per record, so duration reporting
    /// has a start point rather than beginning at the first post-migration edit.
    /// </summary>
    [Fact]
    public async Task Migration_backfills_an_opening_history_row_per_opportunity()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await MigrateToPreviousAsync(tenantId, "history");
        var opportunityId = Guid.NewGuid();

        await SeedLegacyOpportunityAsync(
            connectionString, tenantId, Guid.NewGuid(), opportunityId, "Historic deal", "Proposal");

        await ApplyMigrationAsync(connectionString, tenantId);

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var change = await db.StageChanges.SingleAsync(
            c => c.RecordId == opportunityId && c.RecordType == PipelineRecordType.Opportunity);

        // An opening balance, not an invented move: it comes from nowhere and has no actor.
        Assert.Null(change.FromStageId);
        Assert.Null(change.ChangedByUserId);

        var opportunity = await db.Opportunities.SingleAsync(o => o.Id == opportunityId);
        Assert.Equal(opportunity.PipelineStageId, change.ToStageId);
    }
}
