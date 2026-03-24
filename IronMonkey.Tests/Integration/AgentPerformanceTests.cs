using IronMonkey.Data;
using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// REPT-03: Dashboard shows agent performance metrics (leads handled, tasks completed, conversion rate)
[Collection("Integration")]
public class AgentPerformanceTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    [Fact(Skip = "not implemented — Phase 5 Plan 04")]
    public async Task GetAgentPerformance_ReturnsLeadsAssignedPerAgent() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 04")]
    public async Task GetAgentPerformance_ReturnsTasksCompletedPerAgent() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 04")]
    public async Task GetAgentPerformance_CalculatesConversionRatePerAgent() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 04")]
    public async Task GetAgentPerformance_CalculatesAvgResponseTime() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 04")]
    public async Task GetAgentPerformance_FiltersByTimePeriod() => await Task.CompletedTask;
}
