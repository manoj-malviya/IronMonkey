using IronMonkey.Data;
using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// REPT-01: Dashboard shows pipeline overview (leads per stage, total value)
[Collection("Integration")]
public class PipelineDashboardTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    [Fact(Skip = "not implemented — Phase 5 Plan 04")]
    public async Task GetPipelineDashboard_ReturnsLeadCountPerStage() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 04")]
    public async Task GetPipelineDashboard_ReturnsTotalDealValuePerStage() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 04")]
    public async Task GetPipelineDashboard_ClosedWonStageMarkedAsTerminal() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 04")]
    public async Task GetPipelineDashboard_LeadsWithoutOpportunityContributeZeroDealValue() => await Task.CompletedTask;
}
