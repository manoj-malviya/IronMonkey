using IronMonkey.Data;
using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// PIPE-02: User can create tasks with due dates, priority, and assignment linked to leads
[Collection("Integration")]
public class TaskTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task CreateTask_LinkedToLead_PersistsWithAllFields()
    {
        // TODO: Create task with Title, DueDate, Priority (Low/Medium/High/Urgent), AssignedToUserId, LeadId
        // Verify all fields persisted correctly
        await Task.CompletedTask;
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task ListTasksByAssignee_ReturnsOnlyAssigneeTasks()
    {
        // TODO: Create tasks for user A and user B, list by user A, verify only user A tasks returned
        await Task.CompletedTask;
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task UpdateTask_ChangesStatusAndPriority()
    {
        // TODO: Create task, update Status to Completed and Priority to High, verify changes persisted
        await Task.CompletedTask;
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task CreateTask_WithoutLeadId_Returns400()
    {
        // TODO: POST to /api/tasks without LeadId, verify 400
        await Task.CompletedTask;
    }
}
