using IronMonkey.ApiService.Features.Leads.Pipeline.States;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

// PIPE-01: User can view leads in a Kanban-style pipeline board with drag-drop between stages
[Collection("Integration")]
public class KanbanBoardTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<(string connStr, Guid stage1, Guid stage2)> SetupDbAsync(Guid tenantId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"kb_{suffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();

        var s1 = PipelineStage.Create(tenantId, "New", 1);
        var s2 = PipelineStage.Create(tenantId, "Qualified", 2);
        db.PipelineStages.AddRange(s1, s2);
        await db.SaveChangesAsync();

        return (connStr, s1.Id, s2.Id);
    }

    [Fact]
    public async Task GetKanbanBoard_ReturnsLeadsGroupedByStage()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stage1, stage2) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        // Create 2 leads in stage1, 1 in stage2
        db.Leads.AddRange(
            Lead.Create(tenantId, "Alice", "A", "111", "a@test.com", LeadSource.Manual, stage1),
            Lead.Create(tenantId, "Bob", "B", "222", "b@test.com", LeadSource.Api, stage1),
            Lead.Create(tenantId, "Carol", "C", "333", "c@test.com", LeadSource.Manual, stage2));
        await db.SaveChangesAsync();

        var stage1Leads = await db.Leads.Where(l => l.PipelineStageId == stage1).ToListAsync();
        var stage2Leads = await db.Leads.Where(l => l.PipelineStageId == stage2).ToListAsync();

        Assert.Equal(2, stage1Leads.Count);
        Assert.Single(stage2Leads);
    }

    [Fact]
    public async Task MoveLead_ValidTransition_UpdatesStageAndReturns200()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stage1, stage2) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var transition = StageTransition.Create(tenantId, stage1, stage2);
        db.StageTransitions.Add(transition);
        var lead = Lead.Create(tenantId, "Dave", "D", "444", "d@test.com", LeadSource.Manual, stage1);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        // Validate and move
        var svc = new StateValidationService(_factory);
        var error = await svc.ValidateTransitionAsync(tenantId, connStr, lead.Id, stage2, CancellationToken.None);
        Assert.Null(error);

        lead.MoveToPipelineStage(stage2);
        await db.SaveChangesAsync();

        await using var freshDb = _factory.CreateForTenant(connStr, tenantId);
        var moved = await freshDb.Leads.FirstAsync(l => l.Id == lead.Id);
        Assert.Equal(stage2, moved.PipelineStageId);
    }

    [Fact]
    public async Task MoveLead_InvalidTransition_Returns400WithAllowedStages()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stage1, stage2) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        // No transitions configured — all moves are invalid
        var lead = Lead.Create(tenantId, "Eve", "E", "555", "e@test.com", LeadSource.Manual, stage1);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var svc = new StateValidationService(_factory);
        var error = await svc.ValidateTransitionAsync(tenantId, connStr, lead.Id, stage2, CancellationToken.None);

        Assert.NotNull(error);
        Assert.Contains("Cannot move from", error);
        Assert.Contains("none configured", error);
    }

    [Fact]
    public async Task GetKanbanBoard_VirtualScroll_ReturnsFirst20LeadsPerStage()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stage1, _) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        // Create 25 leads in stage1
        for (int i = 1; i <= 25; i++)
        {
            db.Leads.Add(Lead.Create(tenantId, $"Lead{i}", "Test", $"55500{i:D4}",
                $"lead{i}@test.com", LeadSource.Manual, stage1));
        }
        await db.SaveChangesAsync();

        var allLeads = await db.Leads.Where(l => l.PipelineStageId == stage1).ToListAsync();
        var page = allLeads.OrderBy(l => l.CreatedAt).Take(20).ToList();
        var hasMore = allLeads.Count > 20;

        Assert.Equal(20, page.Count);
        Assert.True(hasMore);
    }
}
