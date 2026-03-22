using Xunit;

namespace IronMonkey.Tests.Unit;

// PIPE-05: State machine graph setup unit tests
public class StageTransitionConfigurationTests
{
    [Fact(Skip = "Stub: implement in 04-06")]
    public void BuildStateMachine_WithTransitions_CanPermitConfiguredTransitions()
    {
        // TODO: Build Stateless StateMachine with stages [New, Qualified, ClosedWon],
        // transitions [New->Qualified, Qualified->ClosedWon],
        // verify machine.CanFire(New->Qualified)=true, machine.CanFire(New->ClosedWon)=false
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public void BuildStateMachine_NoTransitionsConfigured_CannotFireAnyTransition()
    {
        // TODO: Build StateMachine with 2 stages, no transitions added,
        // verify machine.CanFire() returns false for any target
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public void StageType_ClosedWon_MarkedAsTerminalState()
    {
        // TODO: Create PipelineStage with StageType.ClosedWon, verify IsTerminal property returns true
    }
}
