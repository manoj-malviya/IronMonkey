using IronMonkey.Data;
using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// PIPE-01: User can view leads in a Kanban-style pipeline board with drag-drop between stages
[Collection("Integration")]
public class KanbanBoardTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task GetKanbanBoard_ReturnsLeadsGroupedByStage()
    {
        // TODO: Create leads in 2 stages, call GetKanbanBoard, verify each stage column contains correct leads
        await Task.CompletedTask;
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task MoveLead_ValidTransition_UpdatesStageAndReturns200()
    {
        // TODO: Configure allowed transition A->B, place lead in A, move to B, verify PipelineStageId updated
        await Task.CompletedTask;
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task MoveLead_InvalidTransition_Returns400WithAllowedStages()
    {
        // TODO: Configure only A->B, attempt A->C, verify 400 with message "Cannot move from [A] to [C]. Allowed: [B]"
        await Task.CompletedTask;
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task GetKanbanBoard_WithAgentFilter_ReturnsOnlyFilteredLeads()
    {
        // TODO: Assign leads to two agents, filter by agent A, verify only agent A leads returned
        await Task.CompletedTask;
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task GetKanbanBoard_VirtualScroll_ReturnsFirst20LeadsPerStage()
    {
        // TODO: Create 25 leads in one stage, fetch board, verify column has 20 leads and hasMore=true
        await Task.CompletedTask;
    }
}
