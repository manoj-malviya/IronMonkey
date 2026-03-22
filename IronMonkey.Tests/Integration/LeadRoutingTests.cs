using IronMonkey.Data;
using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// PIPE-03: System supports manual lead assignment and rule-based routing (round-robin, territory)
[Collection("Integration")]
public class LeadRoutingTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task RoundRobinRouting_AssignsLeadsInOrder_AgentABCThenRepeat()
    {
        // TODO: Configure round-robin with agents [A, B, C], create 4 leads, verify assignments: A, B, C, A
        await Task.CompletedTask;
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task TerritoryRouting_ByLeadSource_AssignsToMatchingAgent()
    {
        // TODO: Configure territory routing dimension=LeadSource, map ApiSource->Agent A,
        // create API lead, verify AssignedToUserId = Agent A
        await Task.CompletedTask;
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task Routing_NoMatchingTerritory_LeavesLeadUnassigned()
    {
        // TODO: Configure territory routing with no matching dimension value, verify AssignedToUserId = null
        await Task.CompletedTask;
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task ManualAssignment_OverridesRoutingConfig()
    {
        // TODO: Round-robin enabled, manually set AssignedToUserId via endpoint, verify manual assignment wins
        await Task.CompletedTask;
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task ConfigureRouting_PersistsStrategyAndDimension()
    {
        // TODO: POST /api/routing-config, verify RoutingConfig entity persisted with correct Strategy and Dimension
        await Task.CompletedTask;
    }
}
