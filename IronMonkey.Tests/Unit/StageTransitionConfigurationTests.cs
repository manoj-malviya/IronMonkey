using IronMonkey.Data.Entities;
using Xunit;

namespace IronMonkey.Tests.Unit;

// PIPE-05: State machine graph setup unit tests
public class StageTransitionConfigurationTests
{
    [Fact]
    public void StageType_Default_IsActive()
    {
        var stage = PipelineStage.Create(Guid.NewGuid(), "Test", 1);
        Assert.Equal(StageType.Active, stage.StageType);
        Assert.False(stage.IsTerminal);
    }

    [Fact]
    public void StageType_ClosedWon_IsTerminal()
    {
        var stage = PipelineStage.Create(Guid.NewGuid(), "Won", 1);
        stage.SetStageType(StageType.ClosedWon);
        Assert.True(stage.IsTerminal);
    }

    [Fact]
    public void StageType_ClosedLost_IsTerminal()
    {
        var stage = PipelineStage.Create(Guid.NewGuid(), "Lost", 1);
        stage.SetStageType(StageType.ClosedLost);
        Assert.True(stage.IsTerminal);
    }

    [Fact]
    public void StageType_Entry_IsNotTerminal()
    {
        var stage = PipelineStage.Create(Guid.NewGuid(), "Entry", 1);
        stage.SetStageType(StageType.Entry);
        Assert.False(stage.IsTerminal);
    }
}
