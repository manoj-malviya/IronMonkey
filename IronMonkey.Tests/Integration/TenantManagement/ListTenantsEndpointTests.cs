using IronMonkey.ApiService.Authentication.Endpoints;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration.TenantManagement;

[Collection("Integration")]
public class ListTenantsEndpointTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public ListTenantsEndpointTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private CentralDbContext CreateCentralDbContext()
    {
        var options = new DbContextOptionsBuilder<CentralDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        return new CentralDbContext(options);
    }

    [Fact]
    public async Task ListTenants_WhenTenantsExist_ReturnsSortedList()
    {
        // Arrange
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var tenantA = Tenant.Create("Alpha Corp", $"alpha-{Guid.NewGuid():N}", "Standard", "Active");
        var tenantB = Tenant.Create("Beta Corp", $"beta-{Guid.NewGuid():N}", "Premium", "Active");

        centralDb.Tenants.AddRange(tenantA, tenantB);
        await centralDb.SaveChangesAsync();

        // Act
        var result = await ListTenantsEndpoint.HandleForTest(centralDb, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count >= 2, "Expected at least 2 tenants in the list.");

        // Verify ordered by CreatedAt descending (most recent first)
        for (int i = 0; i < result.Count - 1; i++)
        {
            Assert.True(result[i].CreatedAt >= result[i + 1].CreatedAt,
                "Results should be ordered by CreatedAt descending.");
        }

        // Verify both seeded tenants are present
        Assert.Contains(result, t => t.Name == "Alpha Corp");
        Assert.Contains(result, t => t.Name == "Beta Corp");
    }

    [Fact]
    public async Task ListTenants_WhenEmpty_ReturnsEmptyList()
    {
        // Arrange: use a fresh database per test to avoid polluting with other tests
        var uniqueConnString = _fixture.ConnectionString.Replace("ironmonkey_test", $"tenantlist_empty_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<CentralDbContext>()
            .UseNpgsql(uniqueConnString)
            .Options;
        await using var centralDb = new CentralDbContext(options);
        await centralDb.Database.MigrateAsync();

        // Act
        var result = await ListTenantsEndpoint.HandleForTest(centralDb, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListTenants_TenantSummary_ContainsExpectedFields()
    {
        // Arrange
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var tenantName = $"FieldCheck Corp {Guid.NewGuid():N}";
        var tenant = Tenant.Create(tenantName, $"fieldcheck-{Guid.NewGuid():N}", "Enterprise", "Active");
        centralDb.Tenants.Add(tenant);
        await centralDb.SaveChangesAsync();

        // Act
        var result = await ListTenantsEndpoint.HandleForTest(centralDb, CancellationToken.None);

        // Assert
        var found = result.FirstOrDefault(t => t.Name == tenantName);
        Assert.NotNull(found);
        Assert.NotEqual(Guid.Empty, found.Id);
        Assert.Equal(tenantName, found.Name);
        Assert.Equal("Active", found.Status);
        Assert.Equal("Enterprise", found.SubscriptionPlan);
        Assert.False(found.IsProvisioned); // newly created, not provisioned
        Assert.True(found.CreatedAt > DateTime.MinValue);
    }
}
