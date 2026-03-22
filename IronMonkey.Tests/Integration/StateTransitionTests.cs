using IronMonkey.Data;
using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// PIPE-05: Lead lifecycle follows a configurable state machine with allowed transitions per tenant
[Collection("Integration")]
public class StateTransitionTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task ConfigureTransition_AllowsFromToStage()
    {
        // TODO: POST /api/stage-transitions with FromStageId and ToStageId,
        // verify StageTransition entity persisted in DB
        await Task.CompletedTask;
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task MoveLead_AllowedTransition_Succeeds()
    {
        // TODO: Configure transition A->B, move lead from A to B via endpoint, verify 200 and new PipelineStageId
        await Task.CompletedTask;
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task MoveLead_ForbiddenTransition_Returns400WithAllowedList()
    {
        // TODO: Configure only A->B, attempt A->C via endpoint,
        // verify 400 body contains "Allowed: [B stage name]"
        await Task.CompletedTask;
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task ClosedWon_Stage_ExcludedFromActiveBoardCount()
    {
        // TODO: Create stage with StageType=ClosedWon, place lead in it,
        // verify GetKanbanBoard active count excludes closed stage leads
        await Task.CompletedTask;
    }
}
