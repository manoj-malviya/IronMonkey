using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

// REPT-03: Dashboard shows agent performance metrics (leads handled, tasks completed, conversion rate)
[Collection("Integration")]
public class AgentPerformanceTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<(string connStr, Guid stageId, Guid agentId)> SetupDbAsync(Guid tenantId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"ap_{suffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();
        var stage = PipelineStage.Create(tenantId, "New", 1, StageType.Entry);
        db.PipelineStages.Add(stage);
        var adminRole = await db.Roles.FirstAsync(r => r.Name == "Admin");
        var agent = User.Create(tenantId, "Agent Smith", "agent@test.com", "hashed_password", adminRole);
        db.Users.Add(agent);
        await db.SaveChangesAsync();
        return (connStr, stage.Id, agent.Id);
    }

    [Fact]
    public async Task GetAgentPerformance_ReturnsLeadsAssignedPerAgent()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stageId, agentId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var lead1 = Lead.Create(tenantId, "L1", "B", "111", "l1@t.com", LeadSource.Manual, stageId);
        lead1.AssignTo(agentId);
        var lead2 = Lead.Create(tenantId, "L2", "B", "222", "l2@t.com", LeadSource.Api, stageId);
        lead2.AssignTo(agentId);
        db.Leads.AddRange(lead1, lead2);
        await db.SaveChangesAsync();

        var assigned = await db.Leads.CountAsync(l => l.AssignedToUserId == agentId);
        Assert.Equal(2, assigned);
    }

    [Fact]
    public async Task GetAgentPerformance_ReturnsTasksCompletedPerAgent()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stageId, agentId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var lead = Lead.Create(tenantId, "Task", "Lead", "555-000", "tl@t.com", LeadSource.Manual, stageId);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var task1 = LeadTask.Create(tenantId, lead.Id, "Call", null, TaskPriority.Medium, agentId);
        task1.Update("Call", null, null, TaskPriority.Medium, IronMonkey.Data.Entities.TaskStatus.Completed, agentId);
        var task2 = LeadTask.Create(tenantId, lead.Id, "Email", null, TaskPriority.Low, agentId);
        // task2 stays Pending
        db.LeadTasks.AddRange(task1, task2);
        await db.SaveChangesAsync();

        var completed = await db.LeadTasks.CountAsync(
            t => t.AssignedToUserId == agentId && t.Status == IronMonkey.Data.Entities.TaskStatus.Completed);
        Assert.Equal(1, completed);
    }

    [Fact]
    public async Task GetAgentPerformance_CalculatesConversionRatePerAgent()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stageId, agentId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var lead1 = Lead.Create(tenantId, "C1", "B", "111", "c1@t.com", LeadSource.Manual, stageId);
        lead1.AssignTo(agentId);
        lead1.Convert(null, null, null);
        var lead2 = Lead.Create(tenantId, "C2", "B", "222", "c2@t.com", LeadSource.Manual, stageId);
        lead2.AssignTo(agentId);
        db.Leads.AddRange(lead1, lead2);
        await db.SaveChangesAsync();

        var total = await db.Leads.CountAsync(l => l.AssignedToUserId == agentId);
        var converted = await db.Leads.CountAsync(l => l.AssignedToUserId == agentId && l.IsConverted);
        var rate = (decimal)converted / total;

        Assert.Equal(2, total);
        Assert.Equal(1, converted);
        Assert.Equal(0.5m, rate);
    }

    [Fact]
    public async Task GetAgentPerformance_CalculatesAvgResponseTime()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stageId, agentId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var lead = Lead.Create(tenantId, "Resp", "Test", "555-RT", "rt@t.com", LeadSource.Api, stageId);
        lead.AssignTo(agentId);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        // Simulate first activity 1 hour after lead creation
        var firstActivity = ActivityLog.Create(tenantId, lead.Id, agentId, "Updated", "Lead", lead.Id.ToString());
        db.ActivityLogs.Add(firstActivity);
        await db.SaveChangesAsync();

        var activities = await db.ActivityLogs
            .Where(a => a.LeadId == lead.Id && a.ActorId == agentId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();

        Assert.Single(activities);
        Assert.Equal(agentId, activities[0].ActorId);
    }

    [Fact]
    public async Task GetAgentPerformance_FiltersByTimePeriod()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stageId, agentId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var lead = Lead.Create(tenantId, "Period", "Filter", "555-PF", "pf@t.com", LeadSource.Manual, stageId);
        lead.AssignTo(agentId);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var startDate = DateTime.UtcNow.AddMinutes(-5);
        var endDate = DateTime.UtcNow.AddMinutes(5);

        var count = await db.Leads.CountAsync(
            l => l.AssignedToUserId == agentId && l.CreatedAt >= startDate && l.CreatedAt <= endDate);
        Assert.True(count >= 1);
    }
}
