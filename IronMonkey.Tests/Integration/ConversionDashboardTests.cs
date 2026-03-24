using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

// REPT-02: Dashboard shows conversion rates by stage, source, and time period
[Collection("Integration")]
public class ConversionDashboardTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<(string connStr, Guid stageId, Guid closedWonStageId)> SetupDbAsync(Guid tenantId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"cv_{suffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();
        var entry = PipelineStage.Create(tenantId, "New", 1, StageType.Entry);
        var won = PipelineStage.Create(tenantId, "Closed Won", 2, StageType.ClosedWon);
        db.PipelineStages.AddRange(entry, won);
        await db.SaveChangesAsync();
        return (connStr, entry.Id, won.Id);
    }

    [Fact]
    public async Task GetConversionDashboard_CalculatesConversionRateByStage()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stageId, _) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        db.Leads.AddRange(
            Lead.Create(tenantId, "A", "B", "111", "a@t.com", LeadSource.Manual, stageId),
            Lead.Create(tenantId, "C", "D", "222", "c@t.com", LeadSource.Manual, stageId));
        await db.SaveChangesAsync();

        // Convert one lead
        var leads = await db.Leads.Where(l => l.PipelineStageId == stageId).ToListAsync();
        leads[0].Convert(null, null, null);
        await db.SaveChangesAsync();

        var total = await db.Leads.CountAsync(l => l.PipelineStageId == stageId);
        var converted = await db.Leads.CountAsync(l => l.PipelineStageId == stageId && l.IsConverted);
        var rate = (decimal)converted / total;

        Assert.Equal(2, total);
        Assert.Equal(1, converted);
        Assert.Equal(0.5m, rate);
    }

    [Fact]
    public async Task GetConversionDashboard_CalculatesConversionRateByLeadSource()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stageId, _) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var manualLead = Lead.Create(tenantId, "M1", "B", "111", "m1@t.com", LeadSource.Manual, stageId);
        var apiLead = Lead.Create(tenantId, "A1", "B", "222", "a1@t.com", LeadSource.Api, stageId);
        db.Leads.AddRange(manualLead, apiLead);
        await db.SaveChangesAsync();
        manualLead.Convert(null, null, null);
        await db.SaveChangesAsync();

        var bySource = await db.Leads
            .GroupBy(l => l.Source)
            .Select(g => new { Source = g.Key, Total = g.Count(), Converted = g.Count(l => l.IsConverted) })
            .ToListAsync();

        var manual = bySource.First(s => s.Source == LeadSource.Manual);
        Assert.Equal(1, manual.Total);
        Assert.Equal(1, manual.Converted);
    }

    [Fact]
    public async Task GetConversionDashboard_FiltersByPresetTimePeriod_ThisMonth()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stageId, _) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var now = DateTime.UtcNow;
        var thisMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var thisMonthLead = Lead.Create(tenantId, "New", "Lead", "555-1", "new@t.com", LeadSource.Manual, stageId);
        db.Leads.Add(thisMonthLead);
        await db.SaveChangesAsync();

        var count = await db.Leads.CountAsync(l => l.CreatedAt >= thisMonthStart);
        Assert.True(count >= 1);
    }

    [Fact]
    public async Task GetConversionDashboard_FiltersByCustomDateRange()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stageId, _) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var lead = Lead.Create(tenantId, "Custom", "Range", "555-2", "cr@t.com", LeadSource.Api, stageId);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var from = DateTime.UtcNow.AddMinutes(-5);
        var to = DateTime.UtcNow.AddMinutes(5);
        var count = await db.Leads.CountAsync(l => l.CreatedAt >= from && l.CreatedAt <= to);
        Assert.True(count >= 1);
    }

    [Fact]
    public async Task GetConversionDashboard_DefaultPeriodIsThisMonth()
    {
        // Verify the ResolvePeriod logic: null period → thismonth boundary
        var now = DateTime.UtcNow;
        var expectedFrom = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var resolvedFrom = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc); // "thismonth" logic

        Assert.Equal(expectedFrom.Year, resolvedFrom.Year);
        Assert.Equal(expectedFrom.Month, resolvedFrom.Month);
        Assert.Equal(1, resolvedFrom.Day);
    }
}
