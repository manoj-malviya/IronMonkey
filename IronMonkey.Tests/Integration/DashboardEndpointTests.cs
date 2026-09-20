using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Reports.Dashboard;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;
using TaskStatus = IronMonkey.Data.Entities.TaskStatus;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// Covers the tenant CRM dashboard endpoints by invoking the real handlers, so the
/// aggregation under test is the aggregation that ships — not a copy of the same LINQ
/// rewritten in the test.
/// </summary>
[Collection("Integration")]
public class DashboardEndpointTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    /// <summary>
    /// Stands in for the request-scoped tenant resolution. The real ITenantService reads the
    /// tenant from the JWT; here it is pinned so a test can assert that a handler given
    /// tenant A's identity cannot observe tenant B's rows.
    /// </summary>
    private sealed class FixedTenantService(Guid tenantId, string connectionString) : ITenantService
    {
        public Guid GetCurrentTenantId() => tenantId;
        public Task<string> GetConnectionStringAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(connectionString);
    }

    private async Task<string> CreateTenantDbAsync(Guid tenantId, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connectionString = fixture.ConnectionString.Replace("ironmonkey_test", $"dash_{label}_{suffix}");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    private TenantDbContext Db(string connectionString, Guid tenantId)
        => _factory.CreateForTenant(connectionString, tenantId);

    private FixedTenantService Tenant(Guid tenantId, string connectionString)
        => new(tenantId, connectionString);

    /// <summary>
    /// Writes a row with an explicit CreatedAt. GenerateTimestamps overwrites CreatedAt on
    /// insert, so a test that needs a lead dated in the past has to update it afterwards —
    /// setting the property before SaveChanges is silently discarded.
    /// </summary>
    private static async Task BackdateLeadAsync(TenantDbContext db, Guid leadId, DateTime createdAt)
    {
        await db.Database.ExecuteSqlRawAsync(
            """UPDATE leads SET "CreatedAt" = {0}, "UpdatedAt" = {0} WHERE "Id" = {1}""",
            createdAt, leadId);
    }

    // ── Aggregation correctness ─────────────────────────────────────────────

    [Fact]
    public async Task Summary_CountsLeadsPerStage_AndComputesConversionRate()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "sum");
        await using var db = Db(connectionString, tenantId);

        var entry = PipelineStage.Create(tenantId, "New", 1, StageType.Entry);
        var active = PipelineStage.Create(tenantId, "Qualified", 2, StageType.Active);
        var won = PipelineStage.Create(tenantId, "Closed Won", 3, StageType.ClosedWon);
        db.PipelineStages.AddRange(entry, active, won);
        await db.SaveChangesAsync();

        var contact = Contact.Create(tenantId, "Jane", "555", "jane@t.com");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var opportunity = Opportunity.Create(tenantId, "Deal", contact.Id, DateTime.UtcNow.AddDays(30), "Won");
        opportunity.SetAmount(1000m);
        db.Opportunities.Add(opportunity);
        await db.SaveChangesAsync();

        var l1 = Lead.Create(tenantId, "A", "One", "1", "a@t.com", LeadSource.Manual, entry.Id);
        var l2 = Lead.Create(tenantId, "B", "Two", "2", "b@t.com", LeadSource.Manual, entry.Id);
        var l3 = Lead.Create(tenantId, "C", "Three", "3", "c@t.com", LeadSource.Manual, active.Id);
        var l4 = Lead.Create(tenantId, "D", "Four", "4", "d@t.com", LeadSource.Manual, won.Id);
        l4.Convert(null, contact.Id, opportunity.Id);
        db.Leads.AddRange(l1, l2, l3, l4);
        await db.SaveChangesAsync();

        var result = await GetDashboardSummaryEndpoint.Handle(
            DashboardDateRange.AllTime, null, null,
            Tenant(tenantId, connectionString), _factory, CancellationToken.None);

        var body = result.Value!;

        Assert.Equal(4, body.TotalLeads);
        Assert.Equal(1, body.ConvertedLeads);
        Assert.Equal(0.25m, body.ConversionRate);

        // Open excludes both the converted lead and anything sitting in a terminal stage.
        Assert.Equal(3, body.OpenLeads);

        Assert.True(body.HasAnyStages);
        Assert.Equal(2, body.ByStage.Single(s => s.StageId == entry.Id).LeadCount);
        Assert.Equal(1, body.ByStage.Single(s => s.StageId == active.Id).LeadCount);
        Assert.Equal(1, body.ByStage.Single(s => s.StageId == won.Id).LeadCount);
        Assert.True(body.ByStage.Single(s => s.StageId == won.Id).IsTerminal);
    }

    [Fact]
    public async Task Summary_LeadInTerminalStage_IsNotCountedAsOpen()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "lost");
        await using var db = Db(connectionString, tenantId);

        var entry = PipelineStage.Create(tenantId, "New", 1, StageType.Entry);
        var lost = PipelineStage.Create(tenantId, "Closed Lost", 2, StageType.ClosedLost);
        db.PipelineStages.AddRange(entry, lost);
        await db.SaveChangesAsync();

        // Never converted, but parked in Closed Lost — finished work, not open work.
        db.Leads.AddRange(
            Lead.Create(tenantId, "Open", "Lead", "1", "o@t.com", LeadSource.Manual, entry.Id),
            Lead.Create(tenantId, "Dead", "Lead", "2", "d@t.com", LeadSource.Manual, lost.Id));
        await db.SaveChangesAsync();

        var result = await GetDashboardSummaryEndpoint.Handle(
            DashboardDateRange.AllTime, null, null,
            Tenant(tenantId, connectionString), _factory, CancellationToken.None);

        Assert.Equal(2, result.Value!.TotalLeads);
        Assert.Equal(1, result.Value!.OpenLeads);
        Assert.Equal(0, result.Value!.ConvertedLeads);
    }

    [Fact]
    public async Task Opportunities_SumsValuePerStage_AndSplitsOpenFromWon()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "opp");
        await using var db = Db(connectionString, tenantId);

        var contact = Contact.Create(tenantId, "Jane", "555", "jane@t.com");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var proposal1 = Opportunity.Create(tenantId, "P1", contact.Id, DateTime.UtcNow.AddDays(10), "Proposal");
        proposal1.SetAmount(1000m);
        var proposal2 = Opportunity.Create(tenantId, "P2", contact.Id, DateTime.UtcNow.AddDays(20), "Proposal");
        proposal2.SetAmount(500m);
        var won = Opportunity.Create(tenantId, "W1", contact.Id, DateTime.UtcNow.AddDays(5), "Won");
        won.SetAmount(2500m);
        var lost = Opportunity.Create(tenantId, "L1", contact.Id, DateTime.UtcNow.AddDays(5), "Lost");
        lost.SetAmount(9000m);
        db.Opportunities.AddRange(proposal1, proposal2, won, lost);
        await db.SaveChangesAsync();

        var result = await GetDashboardOpportunitiesEndpoint.Handle(
            DashboardDateRange.AllTime, null, null,
            Tenant(tenantId, connectionString), _factory, CancellationToken.None);

        var body = result.Value!;

        Assert.Equal(4, body.TotalCount);
        Assert.Equal(13000m, body.TotalValue);

        // Open excludes both terminal stages, so the lost deal's 9000 is not "in play".
        Assert.Equal(2, body.OpenCount);
        Assert.Equal(1500m, body.OpenValue);

        Assert.Equal(1, body.WonCount);
        Assert.Equal(2500m, body.WonValue);

        var proposalRow = body.ByStage.Single(s => s.Stage == "Proposal");
        Assert.Equal(2, proposalRow.Count);
        Assert.Equal(1500m, proposalRow.TotalValue);
        Assert.False(proposalRow.IsTerminal);
        Assert.True(body.ByStage.Single(s => s.Stage == "Lost").IsTerminal);
    }

    // ── Date boundaries ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, true)]    // created today — inside
    [InlineData(-6, true)]   // first day of a 7-day window — inside
    [InlineData(-7, false)]  // one day before the window opens — outside
    public async Task Summary_Last7Days_IncludesExactlyTheWindow(int dayOffset, bool expectedInRange)
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "bound");
        await using var db = Db(connectionString, tenantId);

        var stage = PipelineStage.Create(tenantId, "New", 1, StageType.Entry);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync();

        var lead = Lead.Create(tenantId, "Edge", "Case", "1", "e@t.com", LeadSource.Manual, stage.Id);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        // Noon avoids any ambiguity about which calendar day the row belongs to.
        await BackdateLeadAsync(db, lead.Id, DateTime.UtcNow.Date.AddDays(dayOffset).AddHours(12));

        var result = await GetDashboardSummaryEndpoint.Handle(
            DashboardDateRange.Last7Days, null, null,
            Tenant(tenantId, connectionString), _factory, CancellationToken.None);

        Assert.Equal(expectedInRange ? 1 : 0, result.Value!.TotalLeads);
    }

    [Fact]
    public async Task Summary_CustomRange_IncludesTheLastInstantOfTheEndDate()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "endday");
        await using var db = Db(connectionString, tenantId);

        var stage = PipelineStage.Create(tenantId, "New", 1, StageType.Entry);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync();

        var lead = Lead.Create(tenantId, "Late", "Night", "1", "l@t.com", LeadSource.Manual, stage.Id);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var endDate = DateTime.UtcNow.Date.AddDays(-1);

        // 23:59:59.999999 on the final day: the case an inclusive "<= end.Date" bound drops
        // and a half-open "< end.Date + 1 day" bound keeps.
        await BackdateLeadAsync(db, lead.Id, endDate.AddDays(1).AddTicks(-1));

        var result = await GetDashboardSummaryEndpoint.Handle(
            DashboardDateRange.Custom, endDate.AddDays(-3), endDate,
            Tenant(tenantId, connectionString), _factory, CancellationToken.None);

        Assert.Equal(1, result.Value!.TotalLeads);
    }

    [Fact]
    public void DateRange_InvertedCustomRange_IsNormalizedRatherThanReturningNothing()
    {
        var now = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

        var range = DashboardDateRange.Resolve(
            DashboardDateRange.Custom,
            from: new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc),
            to: new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc),
            nowUtc: now);

        Assert.True(range.From < range.ToExclusive);
        Assert.Equal(new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc), range.From);
        Assert.Equal(new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc), range.ToInclusive);
    }

    [Fact]
    public void DateRange_ExplicitDatesWithoutPreset_AreTreatedAsCustom()
    {
        var now = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

        var range = DashboardDateRange.Resolve(
            preset: null,
            from: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            to: new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc),
            nowUtc: now);

        Assert.Equal(DashboardDateRange.Custom, range.Preset);
        Assert.Equal(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), range.From);
        Assert.Equal(new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc), range.ToExclusive);
    }

    [Fact]
    public void DateRange_UnknownPreset_FallsBackToLast30Days()
    {
        var now = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

        var range = DashboardDateRange.Resolve("not-a-preset", null, null, now);

        Assert.Equal(DashboardDateRange.Last30Days, range.Preset);
        Assert.Equal(new DateTime(2026, 5, 17, 0, 0, 0, DateTimeKind.Utc), range.From);
        Assert.Equal(new DateTime(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc), range.ToExclusive);
    }

    // ── Empty data ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Summary_EmptyTenant_ReturnsZerosAndFlagsMissingPipeline()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "empty");

        var result = await GetDashboardSummaryEndpoint.Handle(
            DashboardDateRange.AllTime, null, null,
            Tenant(tenantId, connectionString), _factory, CancellationToken.None);

        var body = result.Value!;

        Assert.Equal(0, body.TotalLeads);
        Assert.Equal(0, body.OpenLeads);

        // Zero rather than NaN: dividing by a zero denominator must not be attempted.
        Assert.Equal(0m, body.ConversionRate);

        // The UI uses this to offer "set up your pipeline" instead of an empty chart.
        Assert.False(body.HasAnyStages);
        Assert.Empty(body.ByStage);
    }

    [Fact]
    public async Task Attention_EmptyTenant_ReturnsEmptyListsNotNull()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "attempty");

        var result = await GetDashboardAttentionEndpoint.Handle(
            Tenant(tenantId, connectionString), _factory, CancellationToken.None);

        Assert.Equal(0, result.Value!.OverdueTaskCount);
        Assert.Empty(result.Value!.OverdueTasks);
        Assert.Equal(0, result.Value!.StaleLeadCount);
        Assert.Empty(result.Value!.StaleLeads);
    }

    [Fact]
    public async Task Opportunities_EmptyTenant_ReturnsZeroTotals()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "oppempty");

        var result = await GetDashboardOpportunitiesEndpoint.Handle(
            DashboardDateRange.AllTime, null, null,
            Tenant(tenantId, connectionString), _factory, CancellationToken.None);

        Assert.Equal(0, result.Value!.TotalCount);
        Assert.Equal(0m, result.Value!.TotalValue);
        Assert.Empty(result.Value!.ByStage);
    }

    // ── Attention panel ─────────────────────────────────────────────────────

    [Fact]
    public async Task Attention_CountsOverdueTasks_AndIgnoresCompletedOnes()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "overdue");
        await using var db = Db(connectionString, tenantId);

        var stage = PipelineStage.Create(tenantId, "New", 1, StageType.Entry);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync();

        var lead = Lead.Create(tenantId, "Task", "Owner", "1", "t@t.com", LeadSource.Manual, stage.Id);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var overdue = LeadTask.Create(
            tenantId, lead.Id, "Call back", DateTime.UtcNow.AddDays(-3), TaskPriority.High, null);
        var alsoOverdue = LeadTask.Create(
            tenantId, lead.Id, "Send quote", DateTime.UtcNow.AddDays(-1), TaskPriority.Medium, null);

        // Past due but finished — not outstanding work, so it must not be counted.
        var doneButLate = LeadTask.Create(
            tenantId, lead.Id, "Already handled", DateTime.UtcNow.AddDays(-5), TaskPriority.Low, null);
        doneButLate.Update(
            "Already handled", null, DateTime.UtcNow.AddDays(-5), TaskPriority.Low, TaskStatus.Completed, null);

        // Due in the future — not overdue yet.
        var upcoming = LeadTask.Create(
            tenantId, lead.Id, "Later", DateTime.UtcNow.AddDays(5), TaskPriority.Medium, null);

        // No due date at all — cannot be overdue.
        var undated = LeadTask.Create(
            tenantId, lead.Id, "Someday", null, TaskPriority.Low, null);

        db.LeadTasks.AddRange(overdue, alsoOverdue, doneButLate, upcoming, undated);
        await db.SaveChangesAsync();

        var result = await GetDashboardAttentionEndpoint.Handle(
            Tenant(tenantId, connectionString), _factory, CancellationToken.None);

        Assert.Equal(2, result.Value!.OverdueTaskCount);

        // Most overdue first, so the top of the panel is the most urgent item.
        Assert.Equal("Call back", result.Value!.OverdueTasks.First().Title);
        Assert.All(result.Value!.OverdueTasks, t => Assert.True(t.DaysOverdue >= 0));
    }

    [Fact]
    public async Task Attention_FlagsStaleLeads_ButNotConvertedOrTerminalOnes()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "stale");
        await using var db = Db(connectionString, tenantId);

        var entry = PipelineStage.Create(tenantId, "New", 1, StageType.Entry);
        var lost = PipelineStage.Create(tenantId, "Closed Lost", 2, StageType.ClosedLost);
        db.PipelineStages.AddRange(entry, lost);
        await db.SaveChangesAsync();

        var contact = Contact.Create(tenantId, "Jane", "555", "jane@t.com");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var stale = Lead.Create(tenantId, "Stale", "Lead", "1", "s@t.com", LeadSource.Manual, entry.Id);
        var fresh = Lead.Create(tenantId, "Fresh", "Lead", "2", "f@t.com", LeadSource.Manual, entry.Id);
        var oldButLost = Lead.Create(tenantId, "Old", "Lost", "3", "ol@t.com", LeadSource.Manual, lost.Id);
        var oldButConverted = Lead.Create(tenantId, "Old", "Won", "4", "ow@t.com", LeadSource.Manual, entry.Id);
        oldButConverted.Convert(null, contact.Id, null);
        db.Leads.AddRange(stale, fresh, oldButLost, oldButConverted);
        await db.SaveChangesAsync();

        var longAgo = DateTime.UtcNow.AddDays(-40);
        await BackdateLeadAsync(db, stale.Id, longAgo);
        await BackdateLeadAsync(db, oldButLost.Id, longAgo);
        await BackdateLeadAsync(db, oldButConverted.Id, longAgo);

        var result = await GetDashboardAttentionEndpoint.Handle(
            Tenant(tenantId, connectionString), _factory, CancellationToken.None);

        // Only the genuinely neglected open lead: the lost one is finished and the
        // converted one is a success, so ageing in either is expected, not a problem.
        Assert.Equal(1, result.Value!.StaleLeadCount);
        Assert.Equal("Stale Lead", result.Value!.StaleLeads.Single().LeadName);
        Assert.True(result.Value!.StaleLeads.Single().DaysSinceActivity >= 14);
    }

    // ── Recent activity / conversions ───────────────────────────────────────

    [Fact]
    public async Task Activity_ReturnsRecentConversions_IncludingOnesWithoutAnOpportunity()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "conv");
        await using var db = Db(connectionString, tenantId);

        var stage = PipelineStage.Create(tenantId, "New", 1, StageType.Entry);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync();

        var contact = Contact.Create(tenantId, "Jane", "555", "jane@t.com");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var opportunity = Opportunity.Create(tenantId, "Big Deal", contact.Id, DateTime.UtcNow.AddDays(30), "Won");
        opportunity.SetAmount(7500m);
        db.Opportunities.Add(opportunity);
        await db.SaveChangesAsync();

        var withOpportunity = Lead.Create(tenantId, "Has", "Deal", "1", "h@t.com", LeadSource.Manual, stage.Id);
        withOpportunity.Convert(null, contact.Id, opportunity.Id);

        // The contact-only conversion path: converted, but no opportunity was created.
        var withoutOpportunity = Lead.Create(tenantId, "No", "Deal", "2", "n@t.com", LeadSource.Manual, stage.Id);
        withoutOpportunity.Convert(null, contact.Id, null);

        db.Leads.AddRange(withOpportunity, withoutOpportunity);
        await db.SaveChangesAsync();

        var result = await GetDashboardActivityEndpoint.Handle(
            DashboardDateRange.AllTime, null, null,
            Tenant(tenantId, connectionString), _factory, CancellationToken.None);

        Assert.Equal(2, result.Value!.ConversionCount);

        var deal = result.Value!.RecentConversions.Single(c => c.LeadName == "Has Deal");
        Assert.Equal("Big Deal", deal.OpportunityTitle);
        Assert.Equal(7500m, deal.Amount);

        // Must still appear rather than being dropped by an inner join.
        var noDeal = result.Value!.RecentConversions.Single(c => c.LeadName == "No Deal");
        Assert.Null(noDeal.OpportunityId);
        Assert.Null(noDeal.Amount);
    }

    // ── Tenant isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task Summary_DoesNotCountAnotherTenantsLeads()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Both tenants share one physical database here, which is the strictest version of
        // the test: only the global query filter separates them, so a handler that dropped
        // the filter would read across the boundary and fail this.
        var connectionString = await CreateTenantDbAsync(tenantA, "iso");

        await using (var dbA = Db(connectionString, tenantA))
        {
            var stage = PipelineStage.Create(tenantA, "New", 1, StageType.Entry);
            dbA.PipelineStages.Add(stage);
            await dbA.SaveChangesAsync();

            dbA.Leads.Add(Lead.Create(tenantA, "A", "Lead", "1", "a@t.com", LeadSource.Manual, stage.Id));
            await dbA.SaveChangesAsync();
        }

        await using (var dbB = Db(connectionString, tenantB))
        {
            var stage = PipelineStage.Create(tenantB, "New", 1, StageType.Entry);
            dbB.PipelineStages.Add(stage);
            await dbB.SaveChangesAsync();

            dbB.Leads.AddRange(
                Lead.Create(tenantB, "B", "One", "2", "b1@t.com", LeadSource.Manual, stage.Id),
                Lead.Create(tenantB, "B", "Two", "3", "b2@t.com", LeadSource.Manual, stage.Id));
            await dbB.SaveChangesAsync();
        }

        var resultA = await GetDashboardSummaryEndpoint.Handle(
            DashboardDateRange.AllTime, null, null,
            Tenant(tenantA, connectionString), _factory, CancellationToken.None);

        var resultB = await GetDashboardSummaryEndpoint.Handle(
            DashboardDateRange.AllTime, null, null,
            Tenant(tenantB, connectionString), _factory, CancellationToken.None);

        Assert.Equal(1, resultA.Value!.TotalLeads);
        Assert.Single(resultA.Value!.ByStage);

        Assert.Equal(2, resultB.Value!.TotalLeads);
        Assert.Single(resultB.Value!.ByStage);
    }

    [Fact]
    public async Task Opportunities_DoNotLeakAcrossTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantA, "oppiso");

        await using (var dbA = Db(connectionString, tenantA))
        {
            var contact = Contact.Create(tenantA, "A Contact", "1", "a@t.com");
            dbA.Contacts.Add(contact);
            await dbA.SaveChangesAsync();

            var opp = Opportunity.Create(tenantA, "A Deal", contact.Id, DateTime.UtcNow.AddDays(10), "Proposal");
            opp.SetAmount(100m);
            dbA.Opportunities.Add(opp);
            await dbA.SaveChangesAsync();
        }

        await using (var dbB = Db(connectionString, tenantB))
        {
            var contact = Contact.Create(tenantB, "B Contact", "2", "b@t.com");
            dbB.Contacts.Add(contact);
            await dbB.SaveChangesAsync();

            var opp = Opportunity.Create(tenantB, "B Deal", contact.Id, DateTime.UtcNow.AddDays(10), "Proposal");
            opp.SetAmount(9999m);
            dbB.Opportunities.Add(opp);
            await dbB.SaveChangesAsync();
        }

        var resultA = await GetDashboardOpportunitiesEndpoint.Handle(
            DashboardDateRange.AllTime, null, null,
            Tenant(tenantA, connectionString), _factory, CancellationToken.None);

        Assert.Equal(1, resultA.Value!.TotalCount);
        Assert.Equal(100m, resultA.Value!.TotalValue);
    }

    [Fact]
    public async Task Attention_DoesNotSurfaceAnotherTenantsOverdueTasks()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantA, "attiso");

        await using (var dbB = Db(connectionString, tenantB))
        {
            var stage = PipelineStage.Create(tenantB, "New", 1, StageType.Entry);
            dbB.PipelineStages.Add(stage);
            await dbB.SaveChangesAsync();

            var lead = Lead.Create(tenantB, "B", "Lead", "1", "b@t.com", LeadSource.Manual, stage.Id);
            dbB.Leads.Add(lead);
            await dbB.SaveChangesAsync();

            dbB.LeadTasks.Add(LeadTask.Create(
                tenantB, lead.Id, "B's overdue task", DateTime.UtcNow.AddDays(-9), TaskPriority.High, null));
            await dbB.SaveChangesAsync();
        }

        var resultA = await GetDashboardAttentionEndpoint.Handle(
            Tenant(tenantA, connectionString), _factory, CancellationToken.None);

        Assert.Equal(0, resultA.Value!.OverdueTaskCount);
        Assert.Empty(resultA.Value!.OverdueTasks);
    }
}
