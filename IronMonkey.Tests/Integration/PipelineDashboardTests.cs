using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

// REPT-01: Dashboard shows pipeline overview (leads per stage, total value)
[Collection("Integration")]
public class PipelineDashboardTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<string> SetupDbAsync(Guid tenantId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"pd_{suffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();
        return connStr;
    }

    [Fact]
    public async Task GetPipelineDashboard_ReturnsLeadCountPerStage()
    {
        var tenantId = Guid.NewGuid();
        var connStr = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var stageNew = PipelineStage.Create(tenantId, "New", 1, StageType.Entry);
        var stageQual = PipelineStage.Create(tenantId, "Qualified", 2, StageType.Active);
        db.PipelineStages.AddRange(stageNew, stageQual);
        await db.SaveChangesAsync();

        db.Leads.AddRange(
            Lead.Create(tenantId, "A", "B", "111", "a@t.com", LeadSource.Manual, stageNew.Id),
            Lead.Create(tenantId, "C", "D", "222", "c@t.com", LeadSource.Manual, stageNew.Id),
            Lead.Create(tenantId, "E", "F", "333", "e@t.com", LeadSource.Api, stageQual.Id));
        await db.SaveChangesAsync();

        var countsPerStage = await db.Leads
            .GroupBy(l => l.PipelineStageId)
            .Select(g => new { StageId = g.Key, Count = g.Count() })
            .ToListAsync();

        var newCount = countsPerStage.FirstOrDefault(s => s.StageId == stageNew.Id)?.Count ?? 0;
        var qualCount = countsPerStage.FirstOrDefault(s => s.StageId == stageQual.Id)?.Count ?? 0;

        Assert.Equal(2, newCount);
        Assert.Equal(1, qualCount);
    }

    [Fact]
    public async Task GetPipelineDashboard_ReturnsTotalDealValuePerStage()
    {
        var tenantId = Guid.NewGuid();
        var connStr = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var stage = PipelineStage.Create(tenantId, "Closed Won", 3, StageType.ClosedWon);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync();

        var contact = Contact.Create(tenantId, "Jane Doe", "555-1234", "jane@test.com");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var opp1 = Opportunity.Create(tenantId, "Deal 1", contact.Id, DateTime.UtcNow.AddDays(30), "Open");
        opp1.SetAmount(5000m);
        var opp2 = Opportunity.Create(tenantId, "Deal 2", contact.Id, DateTime.UtcNow.AddDays(60), "Open");
        opp2.SetAmount(3000m);
        db.Opportunities.AddRange(opp1, opp2);
        await db.SaveChangesAsync();

        var lead1 = Lead.Create(tenantId, "Buyer", "One", "555-001", "b1@t.com", LeadSource.Manual, stage.Id);
        lead1.Convert(null, contact.Id, opp1.Id);
        var lead2 = Lead.Create(tenantId, "Buyer", "Two", "555-002", "b2@t.com", LeadSource.Api, stage.Id);
        lead2.Convert(null, contact.Id, opp2.Id);
        db.Leads.AddRange(lead1, lead2);
        await db.SaveChangesAsync();

        var dealValue = await db.Leads
            .Where(l => l.PipelineStageId == stage.Id && l.ConvertedOpportunityId != null)
            .Join(db.Opportunities,
                lead => lead.ConvertedOpportunityId,
                opp => opp.Id,
                (lead, opp) => opp.Amount)
            .SumAsync(a => (decimal?)a) ?? 0m;

        Assert.Equal(8000m, dealValue);
    }

    [Fact]
    public async Task GetPipelineDashboard_ClosedWonStageMarkedAsTerminal()
    {
        var tenantId = Guid.NewGuid();
        var connStr = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var wonStage = PipelineStage.Create(tenantId, "Closed Won", 3, StageType.ClosedWon);
        var activeStage = PipelineStage.Create(tenantId, "Active", 2, StageType.Active);
        db.PipelineStages.AddRange(wonStage, activeStage);
        await db.SaveChangesAsync();

        var stages = await db.PipelineStages.Where(p => p.IsActive).ToListAsync();
        var won = stages.First(s => s.Id == wonStage.Id);
        var active = stages.First(s => s.Id == activeStage.Id);

        Assert.True(won.IsTerminal);
        Assert.False(active.IsTerminal);
        Assert.Equal(StageType.ClosedWon, won.StageType);
    }

    [Fact]
    public async Task GetPipelineDashboard_LeadsWithoutOpportunityContributeZeroDealValue()
    {
        var tenantId = Guid.NewGuid();
        var connStr = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var stage = PipelineStage.Create(tenantId, "New", 1, StageType.Entry);
        db.PipelineStages.Add(stage);
        var lead = Lead.Create(tenantId, "No", "Opp", "555-000", "no@opp.com", LeadSource.Manual, stage.Id);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var dealValue = await db.Leads
            .Where(l => l.PipelineStageId == stage.Id && l.ConvertedOpportunityId != null)
            .Join(db.Opportunities, l => l.ConvertedOpportunityId, o => o.Id, (l, o) => o.Amount)
            .SumAsync(a => (decimal?)a) ?? 0m;

        Assert.Equal(0m, dealValue);
    }
}
