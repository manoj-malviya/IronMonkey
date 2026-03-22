using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

// PIPE-02: User can create tasks with due dates, priority, and assignment linked to leads
[Collection("Integration")]
public class TaskTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<(string connStr, Guid stageId, Guid leadId)> SetupDbAsync(Guid tenantId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"task_{suffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();

        var stage = PipelineStage.Create(tenantId, "New", 1);
        db.PipelineStages.Add(stage);
        var lead = Lead.Create(tenantId, "Test", "Lead", "555-0000", "test@test.com", LeadSource.Manual, stage.Id);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        return (connStr, stage.Id, lead.Id);
    }

    [Fact]
    public async Task CreateTask_LinkedToLead_PersistsWithAllFields()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var userId = Guid.NewGuid();
        var dueDate = DateTime.UtcNow.AddDays(3);
        var task = LeadTask.Create(tenantId, leadId, "Follow up call", dueDate, TaskPriority.High, userId);
        db.LeadTasks.Add(task);
        await db.SaveChangesAsync();

        await using var freshDb = _factory.CreateForTenant(connStr, tenantId);
        var saved = await freshDb.LeadTasks.FirstAsync(t => t.Id == task.Id);

        Assert.Equal("Follow up call", saved.Title);
        Assert.Equal(TaskPriority.High, saved.Priority);
        Assert.Equal(IronMonkey.Data.Entities.TaskStatus.Pending, saved.Status);
        Assert.Equal(userId, saved.AssignedToUserId);
        Assert.Equal(leadId, saved.LeadId);
    }

    [Fact]
    public async Task ListTasksByAssignee_ReturnsOnlyAssigneeTasks()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        db.LeadTasks.AddRange(
            LeadTask.Create(tenantId, leadId, "Task for A", null, TaskPriority.Low, userA),
            LeadTask.Create(tenantId, leadId, "Task for B", null, TaskPriority.Low, userB));
        await db.SaveChangesAsync();

        var userATasks = await db.LeadTasks.Where(t => t.AssignedToUserId == userA).ToListAsync();

        Assert.Single(userATasks);
        Assert.Equal("Task for A", userATasks[0].Title);
    }

    [Fact]
    public async Task UpdateTask_ChangesStatusAndPriority()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var task = LeadTask.Create(tenantId, leadId, "Initial", null, TaskPriority.Low, null);
        db.LeadTasks.Add(task);
        await db.SaveChangesAsync();

        task.Update("Updated title", "Some description", DateTime.UtcNow.AddDays(1),
            TaskPriority.Urgent, IronMonkey.Data.Entities.TaskStatus.Completed, null);
        await db.SaveChangesAsync();

        await using var freshDb = _factory.CreateForTenant(connStr, tenantId);
        var updated = await freshDb.LeadTasks.FirstAsync(t => t.Id == task.Id);

        Assert.Equal(TaskPriority.Urgent, updated.Priority);
        Assert.Equal(IronMonkey.Data.Entities.TaskStatus.Completed, updated.Status);
        Assert.Equal("Updated title", updated.Title);
    }
}
