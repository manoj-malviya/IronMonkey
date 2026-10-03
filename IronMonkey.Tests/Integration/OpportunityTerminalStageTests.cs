using IronMonkey.ApiService.Features.Configuration;
using IronMonkey.ApiService.Features.Opportunities;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// Closing an opportunity is decided by the stage's <see cref="StageType"/>, never by its
/// name. The regression these guard against is a tenant renaming "Lost" to "Declined" and
/// discovering that close logic, the loss-reason prompt, the dashboard's open/won split and
/// the exclusion of terminal work have all quietly stopped working.
/// </summary>
[Collection("Integration")]
public class OpportunityTerminalStageTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<string> CreateDbAsync(Guid tenantId, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connectionString = fixture.ConnectionString.Replace("ironmonkey_test", $"oppterm_{label}_{suffix}");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    /// <summary>
    /// Stages given deliberately non-default names, so any assertion that passes here cannot
    /// be passing because it matched "Won" or "Lost".
    /// </summary>
    private static (PipelineStage Entry, PipelineStage Secured, PipelineStage Declined) RenamedStages(Guid tenantId)
        => (
            PipelineStage.CreateFor(tenantId, PipelineRecordType.Opportunity, "Initial Review", 1, StageType.Entry),
            PipelineStage.CreateFor(tenantId, PipelineRecordType.Opportunity, "Order Secured", 2, StageType.ClosedWon),
            PipelineStage.CreateFor(tenantId, PipelineRecordType.Opportunity, "Declined", 3, StageType.ClosedLost));

    [Fact]
    public async Task A_renamed_lost_stage_still_closes_the_deal_as_lost()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "declined");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var (entry, _, declined) = RenamedStages(tenantId);
        db.PipelineStages.AddRange(entry, declined);

        var contact = Contact.Create(tenantId, "Jane", "555", "jane@t.com");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var deal = Opportunity.Create(tenantId, "Deal", contact.Id, DateTime.UtcNow, entry.Id);
        db.Opportunities.Add(deal);
        await db.SaveChangesAsync();

        // Resolved by TYPE, with no mention of the word "Lost" anywhere.
        var lostStage = await OpportunityStageResolver.GetTerminalAsync(
            db, StageType.ClosedLost, CancellationToken.None);

        Assert.NotNull(lostStage);
        Assert.Equal("Declined", lostStage!.Name);

        deal.MarkAsLost(lostStage.Id, "Chose a competitor");
        await db.SaveChangesAsync();

        var closed = await db.Opportunities.Include(o => o.Stage).SingleAsync(o => o.Id == deal.Id);
        Assert.Equal(StageType.ClosedLost, closed.Stage.StageType);
        Assert.True(closed.Stage.IsTerminal);
        Assert.Equal("Chose a competitor", closed.LossReason);
    }

    [Fact]
    public async Task A_renamed_won_stage_is_still_counted_as_won()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "secured");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var (entry, secured, _) = RenamedStages(tenantId);
        db.PipelineStages.AddRange(entry, secured);

        var contact = Contact.Create(tenantId, "Jane", "555", "jane@t.com");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var won = Opportunity.Create(tenantId, "Won deal", contact.Id, DateTime.UtcNow, secured.Id);
        won.SetAmount(5000m);
        var open = Opportunity.Create(tenantId, "Open deal", contact.Id, DateTime.UtcNow, entry.Id);
        open.SetAmount(1000m);
        db.Opportunities.AddRange(won, open);
        await db.SaveChangesAsync();

        var wonValue = await db.Opportunities
            .Where(o => o.Stage.StageType == StageType.ClosedWon)
            .SumAsync(o => o.Amount);

        var openValue = await db.Opportunities
            .Where(o => o.Stage.StageType != StageType.ClosedWon && o.Stage.StageType != StageType.ClosedLost)
            .SumAsync(o => o.Amount);

        Assert.Equal(5000m, wonValue);
        Assert.Equal(1000m, openValue);
    }

    /// <summary>
    /// The inverse trap: an ACTIVE stage a tenant happened to name "Won" must not be counted
    /// as closed business. A name-matching implementation gets this exactly backwards.
    /// </summary>
    [Fact]
    public async Task An_active_stage_named_Won_is_not_treated_as_terminal()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "falsewon");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        // "Won" here means "verbally won, not yet signed" — still open business.
        var misleading = PipelineStage.CreateFor(
            tenantId, PipelineRecordType.Opportunity, "Won", 1, StageType.Active);
        db.PipelineStages.Add(misleading);

        var contact = Contact.Create(tenantId, "Jane", "555", "jane@t.com");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var deal = Opportunity.Create(tenantId, "Deal", contact.Id, DateTime.UtcNow, misleading.Id);
        deal.SetAmount(3000m);
        db.Opportunities.Add(deal);
        await db.SaveChangesAsync();

        var loaded = await db.Opportunities.Include(o => o.Stage).SingleAsync(o => o.Id == deal.Id);

        Assert.False(loaded.Stage.IsTerminal);

        var wonValue = await db.Opportunities
            .Where(o => o.Stage.StageType == StageType.ClosedWon)
            .SumAsync(o => o.Amount);

        Assert.Equal(0m, wonValue);
    }

    [Fact]
    public async Task Terminal_resolution_returns_null_when_the_tenant_has_no_such_stage()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "noterminal");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        db.PipelineStages.Add(PipelineStage.CreateFor(
            tenantId, PipelineRecordType.Opportunity, "Only Stage", 1, StageType.Entry));
        await db.SaveChangesAsync();

        var lost = await OpportunityStageResolver.GetTerminalAsync(
            db, StageType.ClosedLost, CancellationToken.None);

        // Null rather than a wrong stage: the caller reports that the tenant has nowhere to
        // close a deal, instead of silently closing it into an open stage.
        Assert.Null(lost);
    }

    [Fact]
    public async Task Default_entry_stage_is_never_a_terminal_one()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "entry");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        // Ordered so a terminal stage comes first — a naive "first by order" would pick it.
        db.PipelineStages.Add(PipelineStage.CreateFor(
            tenantId, PipelineRecordType.Opportunity, "Declined", 1, StageType.ClosedLost));
        db.PipelineStages.Add(PipelineStage.CreateFor(
            tenantId, PipelineRecordType.Opportunity, "Initial Review", 2, StageType.Entry));
        await db.SaveChangesAsync();

        var entry = await OpportunityStageResolver.GetDefaultEntryAsync(db, CancellationToken.None);

        Assert.NotNull(entry);
        Assert.Equal("Initial Review", entry!.Name);
        Assert.False(entry.IsTerminal);
    }

    /// <summary>
    /// The resolver must never hand back a lead stage — that would move a deal out of the
    /// opportunity pipeline entirely.
    /// </summary>
    [Fact]
    public async Task Stage_resolution_never_returns_a_lead_stage()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "crossresolve");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var leadStage = PipelineStage.Create(tenantId, "Lead Won", 1, StageType.ClosedWon);
        db.PipelineStages.Add(leadStage);
        db.PipelineStages.Add(PipelineStage.CreateFor(
            tenantId, PipelineRecordType.Opportunity, "Initial Review", 1, StageType.Entry));
        await db.SaveChangesAsync();

        Assert.Null(await OpportunityStageResolver.FindAsync(db, leadStage.Id, CancellationToken.None));
        Assert.Null(await OpportunityStageResolver.GetTerminalAsync(
            db, StageType.ClosedWon, CancellationToken.None));
    }

    // ── Stage transition history ────────────────────────────────────────────

    [Fact]
    public async Task A_move_records_which_record_moved_when_and_by_whom()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "history");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var (entry, secured, _) = RenamedStages(tenantId);
        db.PipelineStages.AddRange(entry, secured);

        var contact = Contact.Create(tenantId, "Jane", "555", "jane@t.com");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var deal = Opportunity.Create(tenantId, "Deal", contact.Id, DateTime.UtcNow, entry.Id);
        db.Opportunities.Add(deal);
        await db.SaveChangesAsync();

        var actor = Guid.NewGuid();
        var before = DateTime.UtcNow;

        StageChangeRecorder.Record(
            db, tenantId, PipelineRecordType.Opportunity, deal.Id, entry.Id, secured.Id, actor,
            fromPipelineId: deal.PipelineId, toPipelineId: deal.PipelineId);
        deal.MoveToPipelineStage(secured.Id);
        await db.SaveChangesAsync();

        var change = await db.StageChanges.SingleAsync(c => c.RecordId == deal.Id);

        // Exactly the three things the old StageTransition entity could not express.
        Assert.Equal(deal.Id, change.RecordId);
        Assert.Equal(actor, change.ChangedByUserId);
        Assert.InRange(change.OccurredAt, before.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));

        Assert.Equal(entry.Id, change.FromStageId);
        Assert.Equal(secured.Id, change.ToStageId);
        Assert.Equal(PipelineRecordType.Opportunity, change.RecordType);
    }

    /// <summary>
    /// A record moving back and forth produces one row per move. This is the constraint that
    /// makes StageTransition — uniquely indexed on (tenant, from, to) — unusable as history:
    /// the second A→B move would collide and be lost.
    /// </summary>
    [Fact]
    public async Task Repeating_the_same_move_records_a_second_row()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "repeat");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var a = PipelineStage.CreateFor(tenantId, PipelineRecordType.Opportunity, "A", 1, StageType.Entry);
        var b = PipelineStage.CreateFor(tenantId, PipelineRecordType.Opportunity, "B", 2);
        db.PipelineStages.AddRange(a, b);

        var contact = Contact.Create(tenantId, "Jane", "555", "jane@t.com");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var deal = Opportunity.Create(tenantId, "Deal", contact.Id, DateTime.UtcNow, a.Id);
        db.Opportunities.Add(deal);
        await db.SaveChangesAsync();

        foreach (var (from, to) in new[] { (a.Id, b.Id), (b.Id, a.Id), (a.Id, b.Id) })
        {
            StageChangeRecorder.Record(
                db, tenantId, PipelineRecordType.Opportunity, deal.Id, from, to, null,
                fromPipelineId: deal.PipelineId, toPipelineId: deal.PipelineId);
            deal.MoveToPipelineStage(to);
            await db.SaveChangesAsync();
        }

        Assert.Equal(3, await db.StageChanges.CountAsync(c => c.RecordId == deal.Id));
        Assert.Equal(2, await db.StageChanges.CountAsync(
            c => c.RecordId == deal.Id && c.FromStageId == a.Id && c.ToStageId == b.Id));
    }

    [Fact]
    public async Task A_no_op_move_is_not_recorded()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "noop");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var stage = PipelineStage.CreateFor(
            tenantId, PipelineRecordType.Opportunity, "A", 1, StageType.Entry);
        db.PipelineStages.Add(stage);

        var contact = Contact.Create(tenantId, "Jane", "555", "jane@t.com");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var deal = Opportunity.Create(tenantId, "Deal", contact.Id, DateTime.UtcNow, stage.Id);
        db.Opportunities.Add(deal);
        await db.SaveChangesAsync();

        // Saving the form without touching the stage is not a transition. Recording it would
        // make every time-in-stage figure read as zero.
        var recorded = StageChangeRecorder.Record(
            db, tenantId, PipelineRecordType.Opportunity, deal.Id, stage.Id, stage.Id, null,
            fromPipelineId: deal.PipelineId, toPipelineId: deal.PipelineId);

        Assert.False(recorded);
        await db.SaveChangesAsync();
        Assert.Equal(0, await db.StageChanges.CountAsync(c => c.RecordId == deal.Id));
    }

    /// <summary>
    /// Leads and opportunities share one history model, so a duration query is written once.
    /// </summary>
    [Fact]
    public async Task Leads_and_opportunities_record_into_the_same_history_table()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "both");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var leadStage = PipelineStage.Create(tenantId, "New", 1, StageType.Entry);
        var leadStage2 = PipelineStage.Create(tenantId, "Qualified", 2);
        var oppStage = PipelineStage.CreateFor(
            tenantId, PipelineRecordType.Opportunity, "Initial Review", 1, StageType.Entry);
        var oppStage2 = PipelineStage.CreateFor(tenantId, PipelineRecordType.Opportunity, "Proposal", 2);
        db.PipelineStages.AddRange(leadStage, leadStage2, oppStage, oppStage2);

        var contact = Contact.Create(tenantId, "Jane", "555", "jane@t.com");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var lead = Lead.Create(tenantId, "A", "One", "1", "a@t.com", LeadSource.Manual, leadStage.Id);
        db.Leads.Add(lead);
        var deal = Opportunity.Create(tenantId, "Deal", contact.Id, DateTime.UtcNow, oppStage.Id);
        db.Opportunities.Add(deal);
        await db.SaveChangesAsync();

        StageChangeRecorder.Record(
            db, tenantId, PipelineRecordType.Lead, lead.Id, leadStage.Id, leadStage2.Id, null,
            fromPipelineId: lead.PipelineId, toPipelineId: lead.PipelineId);
        StageChangeRecorder.Record(
            db, tenantId, PipelineRecordType.Opportunity, deal.Id, oppStage.Id, oppStage2.Id, null,
            fromPipelineId: deal.PipelineId, toPipelineId: deal.PipelineId);
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.StageChanges.CountAsync(c => c.RecordType == PipelineRecordType.Lead));
        Assert.Equal(1, await db.StageChanges.CountAsync(c => c.RecordType == PipelineRecordType.Opportunity));

        // The record type is what keeps them apart — the same query shape serves both.
        var leadHistory = await db.StageChanges
            .Where(c => c.RecordType == PipelineRecordType.Lead && c.RecordId == lead.Id)
            .SingleAsync();
        Assert.Equal(leadStage2.Id, leadHistory.ToStageId);
    }

    /// <summary>
    /// <c>StageTransition</c> is the tenant's allowed-edges graph and must keep working as
    /// one. This pins the separation: the new history table does not replace it and the
    /// unique constraint that makes it a configuration table is still in force.
    /// </summary>
    [Fact]
    public async Task The_allowed_transition_graph_is_untouched_and_still_unique()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "graph");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var a = PipelineStage.Create(tenantId, "New", 1, StageType.Entry);
        var b = PipelineStage.Create(tenantId, "Qualified", 2);
        db.PipelineStages.AddRange(a, b);
        await db.SaveChangesAsync();

        db.StageTransitions.Add(StageTransition.Create(tenantId, a.Id, b.Id));
        await db.SaveChangesAsync();

        // Still one row per permitted edge, not an append-only log.
        db.StageTransitions.Add(StageTransition.Create(tenantId, a.Id, b.Id));
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
