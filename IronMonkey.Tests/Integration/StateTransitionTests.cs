using IronMonkey.ApiService.Features.Leads.Pipeline.States;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

// PIPE-05: Lead lifecycle follows a configurable state machine with allowed transitions per tenant
[Collection("Integration")]
public class StateTransitionTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<(string connStr, Guid stageAId, Guid stageBId, Guid stageCId)> SetupDbAsync(Guid tenantId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"st_{suffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();

        var stageA = PipelineStage.Create(tenantId, "New", 1);
        var stageB = PipelineStage.Create(tenantId, "Qualified", 2);
        var stageC = PipelineStage.Create(tenantId, "Closed Won", 3);
        db.PipelineStages.AddRange(stageA, stageB, stageC);
        await db.SaveChangesAsync();

        return (connStr, stageA.Id, stageB.Id, stageC.Id);
    }

    [Fact]
    public async Task ConfigureTransition_AllowsFromToStage()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stageA, stageB, _) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var transition = StageTransition.Create(tenantId, stageA, stageB);
        db.StageTransitions.Add(transition);
        await db.SaveChangesAsync();

        var saved = await db.StageTransitions.FirstAsync(t => t.Id == transition.Id);
        Assert.Equal(stageA, saved.FromStageId);
        Assert.Equal(stageB, saved.ToStageId);
    }

    [Fact]
    public async Task MoveLead_AllowedTransition_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stageA, stageB, _) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var transition = StageTransition.Create(tenantId, stageA, stageB);
        db.StageTransitions.Add(transition);
        var lead = Lead.Create(tenantId, "Alice", "Smith", "555-1234", "alice@test.com", LeadSource.Manual, stageA);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var svc = new StateValidationService(_factory);
        var error = await svc.ValidateTransitionAsync(tenantId, connStr, lead.Id, stageB, CancellationToken.None);

        Assert.Null(error);
    }

    [Fact]
    public async Task MoveLead_ForbiddenTransition_Returns400WithAllowedList()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stageA, stageB, stageC) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        // Only allow A->B, not A->C
        var transition = StageTransition.Create(tenantId, stageA, stageB);
        db.StageTransitions.Add(transition);
        var lead = Lead.Create(tenantId, "Bob", "Jones", "555-9999", "bob@test.com", LeadSource.Manual, stageA);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var svc = new StateValidationService(_factory);
        var error = await svc.ValidateTransitionAsync(tenantId, connStr, lead.Id, stageC, CancellationToken.None);

        Assert.NotNull(error);
        Assert.Contains("Cannot move from", error);
        Assert.Contains("Qualified", error);  // Allowed stage B should appear
    }

    [Fact]
    public async Task ClosedWon_Stage_IsTerminal()
    {
        var tenantId = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"term_{suffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();

        var stage = PipelineStage.Create(tenantId, "Won", 1);
        stage.SetStageType(StageType.ClosedWon);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync();

        Assert.True(stage.IsTerminal);
    }
}
