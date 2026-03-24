using IronMonkey.ApiService.Features.Leads.Pipeline.Routing;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

// PIPE-03: System supports manual lead assignment and rule-based routing (round-robin, territory)
[Collection("Integration")]
public class LeadRoutingTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<(string connStr, Guid stageId)> SetupDbAsync(Guid tenantId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"route_{suffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();
        var stage = PipelineStage.Create(tenantId, "New", 1);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync();
        return (connStr, stage.Id);
    }

    /// <summary>
    /// Create a user in the tenant DB using the seeded TeleCaller role.
    /// Uses EnsureCreatedAsync on a separate context first so HasData seeds roles.
    /// </summary>
    private static async Task<User> CreateUserAsync(TenantDbContext db, Guid tenantId, string name, string email)
    {
        var teleCaller = await db.Roles.FindAsync(Role.TeleCaller.Id);
        teleCaller ??= Role.TeleCaller;
        var user = User.Create(tenantId, name, email, "hash", teleCaller!);
        db.Entry(teleCaller).State = EntityState.Unchanged;
        db.Users.Add(user);
        return user;
    }

    [Fact]
    public async Task RoundRobinRouting_AssignsLeadsInOrder_AgentABCThenRepeat()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        // Create 3 users (agents)
        var userA = await CreateUserAsync(db, tenantId, "Agent A", "agentA@test.com");
        var userB = await CreateUserAsync(db, tenantId, "Agent B", "agentB@test.com");
        var userC = await CreateUserAsync(db, tenantId, "Agent C", "agentC@test.com");

        var config = RoutingConfig.Create(tenantId, RoutingStrategy.RoundRobin, RoutingDimension.LeadSource);
        db.RoutingConfigs.Add(config);
        await db.SaveChangesAsync();

        // Determine ordered agent list (matches LeadRoutingService sort by Id)
        var sortedAgents = new[] { userA.Id, userB.Id, userC.Id }.OrderBy(id => id).ToList();

        var svc = new LeadRoutingService();
        var lead1 = Lead.Create(tenantId, "L1", "Test", "111", "l1@test.com", LeadSource.Manual, stageId);
        var lead2 = Lead.Create(tenantId, "L2", "Test", "222", "l2@test.com", LeadSource.Manual, stageId);
        var lead3 = Lead.Create(tenantId, "L3", "Test", "333", "l3@test.com", LeadSource.Manual, stageId);
        var lead4 = Lead.Create(tenantId, "L4", "Test", "444", "l4@test.com", LeadSource.Manual, stageId);

        var assign1 = await svc.GetNextAssigneeAsync(tenantId, connStr, lead1, db, CancellationToken.None);
        var assign2 = await svc.GetNextAssigneeAsync(tenantId, connStr, lead2, db, CancellationToken.None);
        var assign3 = await svc.GetNextAssigneeAsync(tenantId, connStr, lead3, db, CancellationToken.None);
        var assign4 = await svc.GetNextAssigneeAsync(tenantId, connStr, lead4, db, CancellationToken.None);

        // Strict round-robin: pointer starts at 0, each call advances by 1
        // assign1 = sortedAgents[1], assign2 = sortedAgents[2], assign3 = sortedAgents[0], assign4 = sortedAgents[1]
        Assert.Equal(sortedAgents[1], assign1);
        Assert.Equal(sortedAgents[2], assign2);
        Assert.Equal(sortedAgents[0], assign3);
        Assert.Equal(sortedAgents[1], assign4);  // wraps around
    }

    [Fact]
    public async Task TerritoryRouting_ByLeadSource_AssignsToMatchingAgent()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var agentUser = await CreateUserAsync(db, tenantId, "API Agent", "apiagent@test.com");
        var agentId = agentUser.Id;

        var territoryMap = $"{{\"Api\":\"{agentId}\"}}";
        var config = RoutingConfig.Create(tenantId, RoutingStrategy.Territory,
            RoutingDimension.LeadSource, territoryMap);
        db.RoutingConfigs.Add(config);
        await db.SaveChangesAsync();

        var svc = new LeadRoutingService();
        var lead = Lead.Create(tenantId, "API Lead", "Test", "999", "api@test.com", LeadSource.Api, stageId);

        var assignee = await svc.GetNextAssigneeAsync(tenantId, connStr, lead, db, CancellationToken.None);

        Assert.Equal(agentId, assignee);
    }

    [Fact]
    public async Task Routing_NoMatchingTerritory_LeavesLeadUnassigned()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var agentUser = await CreateUserAsync(db, tenantId, "Web Agent", "webagent@test.com");
        var agentId = agentUser.Id;

        // Map only WebForm leads — not Manual
        var territoryMap = $"{{\"WebForm\":\"{agentId}\"}}";
        var config = RoutingConfig.Create(tenantId, RoutingStrategy.Territory,
            RoutingDimension.LeadSource, territoryMap);
        db.RoutingConfigs.Add(config);
        await db.SaveChangesAsync();

        var svc = new LeadRoutingService();
        var lead = Lead.Create(tenantId, "Manual Lead", "Test", "888", "manual@test.com", LeadSource.Manual, stageId);

        var assignee = await svc.GetNextAssigneeAsync(tenantId, connStr, lead, db, CancellationToken.None);

        Assert.Null(assignee);  // D-16: leave unassigned if no match
    }

    [Fact]
    public async Task ConfigureRouting_PersistsStrategyAndDimension()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var config = RoutingConfig.Create(tenantId, RoutingStrategy.RoundRobin, RoutingDimension.LeadSource);
        db.RoutingConfigs.Add(config);
        await db.SaveChangesAsync();

        var saved = await db.RoutingConfigs.FirstAsync(r => r.TenantId == tenantId);
        Assert.Equal(RoutingStrategy.RoundRobin, saved.Strategy);
        Assert.Equal(RoutingDimension.LeadSource, saved.Dimension);
        Assert.True(saved.IsEnabled);
    }
}
