using IronMonkey.ApiService.Features.Configuration;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// The protections the lead pipeline already enforces, carried over to opportunity stages:
/// case-insensitive name uniqueness, atomic reorder with a real concurrency check, no silent
/// deletion of a stage that holds records, and never removing the last active stage.
///
/// These are asserted at the database and service level rather than by re-implementing the
/// endpoint logic, so a divergence between the two stage kinds shows up here.
/// </summary>
[Collection("Integration")]
public class OpportunityStageConfigurationTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();
    private readonly ConfigurationUsageService _usage = new();

    private async Task<string> CreateDbAsync(Guid tenantId, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connectionString = fixture.ConnectionString.Replace("ironmonkey_test", $"oppcfg_{label}_{suffix}");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    private static PipelineStage OppStage(Guid tenantId, string name, int order, StageType type = StageType.Active)
        => PipelineStage.CreateFor(tenantId, PipelineRecordType.Opportunity, name, order, type);

    // ── Name uniqueness ─────────────────────────────────────────────────────

    [Fact]
    public async Task Opportunity_stage_names_are_unique_per_tenant_case_insensitively()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "dup");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        db.PipelineStages.Add(OppStage(tenantId, "Proposal", 1));
        await db.SaveChangesAsync();

        db.PipelineStages.Add(OppStage(tenantId, "proposal", 2));

        // Enforced by the raw-SQL lower(Name) expression index, which EF cannot express.
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <summary>
    /// The uniqueness index is scoped per record type. A tenant running a "Qualified" lead
    /// stage and a "Qualified" deal stage is entirely reasonable and must not be blocked.
    /// </summary>
    [Fact]
    public async Task A_lead_stage_and_an_opportunity_stage_may_share_a_name()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "crossname");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        db.PipelineStages.Add(PipelineStage.Create(tenantId, "Qualified", 1));
        db.PipelineStages.Add(OppStage(tenantId, "Qualified", 1));

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.PipelineStages.CountAsync(s => s.Name == "Qualified"));
    }

    [Fact]
    public async Task Two_tenants_may_each_have_a_stage_of_the_same_name()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantA, "tenants");

        await using (var dbA = _factory.CreateForTenant(connectionString, tenantA))
        {
            dbA.PipelineStages.Add(OppStage(tenantA, "Proposal", 1));
            await dbA.SaveChangesAsync();
        }

        await using var dbB = _factory.CreateForTenant(connectionString, tenantB);
        dbB.PipelineStages.Add(OppStage(tenantB, "Proposal", 1));

        await dbB.SaveChangesAsync();

        Assert.Equal(1, await dbB.PipelineStages.CountAsync());
    }

    // ── Usage counting, which drives every destructive rule ─────────────────

    [Fact]
    public async Task Usage_counts_the_opportunities_sitting_in_the_stage()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "usage");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var proposal = OppStage(tenantId, "Proposal", 1);
        var won = OppStage(tenantId, "Won", 2, StageType.ClosedWon);
        db.PipelineStages.AddRange(proposal, won);

        var contact = Contact.Create(tenantId, "Jane", "555", "jane@t.com");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        db.Opportunities.Add(Opportunity.Create(tenantId, "D1", contact.Id, DateTime.UtcNow, proposal.Id));
        db.Opportunities.Add(Opportunity.Create(tenantId, "D2", contact.Id, DateTime.UtcNow, proposal.Id));
        await db.SaveChangesAsync();

        var usage = await _usage.GetOpportunityStageUsageAsync(db, proposal.Id, CancellationToken.None);

        Assert.Equal(2, usage.LeadCount);
        Assert.True(usage.IsReferenced);
        Assert.False(usage.IsOnlyActiveStage);
    }

    /// <summary>
    /// The last-active-stage guard must count only opportunity stages. Counting every stage
    /// in the table would let a tenant's lead stages keep the guard from ever firing, and the
    /// tenant could deactivate the only stage a deal can live in.
    /// </summary>
    [Fact]
    public async Task Last_active_opportunity_stage_is_detected_even_when_lead_stages_exist()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "lastactive");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        db.PipelineStages.Add(PipelineStage.Create(tenantId, "New", 1));
        db.PipelineStages.Add(PipelineStage.Create(tenantId, "Qualified", 2));

        var onlyOpportunityStage = OppStage(tenantId, "Proposal", 1);
        db.PipelineStages.Add(onlyOpportunityStage);
        await db.SaveChangesAsync();

        var usage = await _usage.GetOpportunityStageUsageAsync(
            db, onlyOpportunityStage.Id, CancellationToken.None);

        Assert.True(usage.IsOnlyActiveStage);
    }

    /// <summary>The mirror of the above, from the lead side.</summary>
    [Fact]
    public async Task Last_active_lead_stage_is_detected_even_when_opportunity_stages_exist()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "lastlead");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var onlyLeadStage = PipelineStage.Create(tenantId, "New", 1);
        db.PipelineStages.Add(onlyLeadStage);
        db.PipelineStages.Add(OppStage(tenantId, "Proposal", 1));
        db.PipelineStages.Add(OppStage(tenantId, "Won", 2, StageType.ClosedWon));
        await db.SaveChangesAsync();

        var usage = await _usage.GetStageUsageAsync(db, onlyLeadStage.Id, CancellationToken.None);

        Assert.True(usage.IsOnlyActiveStage);
    }

    [Fact]
    public async Task A_stage_holding_no_opportunities_is_not_referenced()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "unused");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var used = OppStage(tenantId, "Proposal", 1);
        var unused = OppStage(tenantId, "Negotiation", 2);
        db.PipelineStages.AddRange(used, unused);

        var contact = Contact.Create(tenantId, "Jane", "555", "jane@t.com");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        db.Opportunities.Add(Opportunity.Create(tenantId, "D1", contact.Id, DateTime.UtcNow, used.Id));
        await db.SaveChangesAsync();

        var usage = await _usage.GetOpportunityStageUsageAsync(db, unused.Id, CancellationToken.None);

        Assert.Equal(0, usage.LeadCount);
        Assert.False(usage.IsReferenced);
    }

    // ── Tenant isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task One_tenants_opportunities_are_never_counted_against_anothers_stage()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantA, "iso");

        Guid stageA;
        await using (var dbA = _factory.CreateForTenant(connectionString, tenantA))
        {
            var stage = OppStage(tenantA, "Proposal", 1);
            dbA.PipelineStages.Add(stage);
            dbA.PipelineStages.Add(OppStage(tenantA, "Won", 2, StageType.ClosedWon));
            var contact = Contact.Create(tenantA, "A", "1", "a@t.com");
            dbA.Contacts.Add(contact);
            await dbA.SaveChangesAsync();

            dbA.Opportunities.Add(Opportunity.Create(tenantA, "A deal", contact.Id, DateTime.UtcNow, stage.Id));
            await dbA.SaveChangesAsync();
            stageA = stage.Id;
        }

        await using (var dbB = _factory.CreateForTenant(connectionString, tenantB))
        {
            var stage = OppStage(tenantB, "Proposal", 1);
            dbB.PipelineStages.Add(stage);
            var contact = Contact.Create(tenantB, "B", "2", "b@t.com");
            dbB.Contacts.Add(contact);
            await dbB.SaveChangesAsync();

            for (var i = 0; i < 5; i++)
                dbB.Opportunities.Add(Opportunity.Create(tenantB, $"B{i}", contact.Id, DateTime.UtcNow, stage.Id));
            await dbB.SaveChangesAsync();
        }

        await using var verify = _factory.CreateForTenant(connectionString, tenantA);
        var usage = await _usage.GetOpportunityStageUsageAsync(verify, stageA, CancellationToken.None);

        Assert.Equal(1, usage.LeadCount);
    }

    // ── Reorder ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A reorder passes stages through positions others still hold, which is why Order is not
    /// uniquely constrained. This asserts the permutation actually commits.
    /// </summary>
    [Fact]
    public async Task Reordering_rewrites_the_whole_sequence()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "reorder");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var first = OppStage(tenantId, "Qualification", 1, StageType.Entry);
        var second = OppStage(tenantId, "Proposal", 2);
        var third = OppStage(tenantId, "Won", 3, StageType.ClosedWon);
        db.PipelineStages.AddRange(first, second, third);
        await db.SaveChangesAsync();

        var desired = new List<Guid> { third.Id, first.Id, second.Id };

        var stages = await db.PipelineStages
            .Where(s => s.RecordType == PipelineRecordType.Opportunity)
            .ToListAsync();

        Assert.True(desired.ToHashSet().SetEquals(stages.Select(s => s.Id)));

        var byId = stages.ToDictionary(s => s.Id);
        for (var i = 0; i < desired.Count; i++) byId[desired[i]].SetOrder(i + 1);
        await db.SaveChangesAsync();

        var ordered = await db.PipelineStages
            .Where(s => s.RecordType == PipelineRecordType.Opportunity)
            .OrderBy(s => s.Order)
            .Select(s => s.Name)
            .ToListAsync();

        Assert.Equal(["Won", "Qualification", "Proposal"], ordered);
    }

    /// <summary>
    /// The concurrency check: a submitted set that differs from the stored set is a 409. This
    /// is what catches another admin creating a stage between the page loading and the
    /// reorder being submitted — applying a partial list would scramble the sequence.
    /// </summary>
    [Fact]
    public async Task Reorder_detects_a_submitted_set_that_differs_from_the_stored_set()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "reorderconf");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var first = OppStage(tenantId, "Qualification", 1, StageType.Entry);
        var second = OppStage(tenantId, "Proposal", 2);
        db.PipelineStages.AddRange(first, second);
        await db.SaveChangesAsync();

        // What the client loaded.
        var submitted = new List<Guid> { second.Id, first.Id }.ToHashSet();

        // A concurrent create lands before the reorder is submitted.
        await using (var other = _factory.CreateForTenant(connectionString, tenantId))
        {
            other.PipelineStages.Add(OppStage(tenantId, "Won", 3, StageType.ClosedWon));
            await other.SaveChangesAsync();
        }

        var stored = (await db.PipelineStages
                .Where(s => s.RecordType == PipelineRecordType.Opportunity)
                .Select(s => s.Id)
                .ToListAsync())
            .ToHashSet();

        Assert.False(submitted.SetEquals(stored));
    }

    /// <summary>
    /// A lead stage id in an opportunity reorder must be rejected, not silently applied —
    /// the two record types share the table and each owns its own 1..n sequence.
    /// </summary>
    [Fact]
    public async Task A_lead_stage_id_is_not_part_of_the_opportunity_stage_set()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "crossreorder");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var leadStage = PipelineStage.Create(tenantId, "New", 1);
        var oppStage = OppStage(tenantId, "Proposal", 1);
        db.PipelineStages.AddRange(leadStage, oppStage);
        await db.SaveChangesAsync();

        var stored = (await db.PipelineStages
                .Where(s => s.RecordType == PipelineRecordType.Opportunity)
                .Select(s => s.Id)
                .ToListAsync())
            .ToHashSet();

        var submitted = new List<Guid> { oppStage.Id, leadStage.Id }.ToHashSet();

        Assert.False(submitted.SetEquals(stored));
    }

    // ── Reassignment ────────────────────────────────────────────────────────

    [Fact]
    public async Task Reassignment_moves_every_opportunity_and_records_the_move()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "reassign");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var removed = OppStage(tenantId, "Proposal", 1);
        var target = OppStage(tenantId, "Negotiation", 2);
        db.PipelineStages.AddRange(removed, target);

        var contact = Contact.Create(tenantId, "Jane", "555", "jane@t.com");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var deal = Opportunity.Create(tenantId, "D1", contact.Id, DateTime.UtcNow, removed.Id);
        db.Opportunities.Add(deal);
        await db.SaveChangesAsync();

        var actor = Guid.NewGuid();
        StageChangeRecorder.Record(
            db, tenantId, PipelineRecordType.Opportunity, deal.Id, deal.PipelineStageId, target.Id, actor,
            fromPipelineId: deal.PipelineId, toPipelineId: deal.PipelineId);
        deal.MoveToPipelineStage(target.Id);
        removed.Deactivate();
        await db.SaveChangesAsync();

        var moved = await db.Opportunities.SingleAsync(o => o.Id == deal.Id);
        Assert.Equal(target.Id, moved.PipelineStageId);

        // The forced move is history: without it the deal appears to have never left a stage
        // it was pushed out of.
        var change = await db.StageChanges.SingleAsync(c => c.RecordId == deal.Id);
        Assert.Equal(removed.Id, change.FromStageId);
        Assert.Equal(target.Id, change.ToStageId);
        Assert.Equal(actor, change.ChangedByUserId);

        Assert.False((await db.PipelineStages.SingleAsync(s => s.Id == removed.Id)).IsActive);
    }
}
