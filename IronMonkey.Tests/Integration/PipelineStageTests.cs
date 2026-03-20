using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// LEAD-02: Pipeline stages are tenant-configured and ordered
[Collection("Integration")]
public class PipelineStageTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    [Fact(Skip = "Wave 0 stub — implement in plan 02-06")]
    public async Task CanCreatePipelineStage_WithNameAndOrder()
    {
        // Arrange
        // Act
        // Assert
        await Task.CompletedTask;
    }

    [Fact(Skip = "Wave 0 stub — implement in plan 02-06")]
    public async Task ListPipelineStages_ReturnsOnlyActiveStages()
    {
        // Arrange
        // Act
        // Assert
        await Task.CompletedTask;
    }

    [Fact(Skip = "Wave 0 stub — implement in plan 02-06")]
    public async Task LeadStageId_MustReferenceValidTenantStage()
    {
        // Arrange
        // Act
        // Assert
        await Task.CompletedTask;
    }
}
