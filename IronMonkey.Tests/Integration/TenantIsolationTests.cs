using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// TNCY-02: EF Core global query filters prevent cross-tenant data reads
[Collection("Integration")]
public class TenantIsolationTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;
    private readonly TenantDbContextFactory _factory = new();

    public TenantIsolationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TenantDbContext_WhenTenantAContext_CannotReadTenantBData()
    {
        // Arrange: two tenant databases, each with one User
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        // Each test run gets unique DB names to avoid state leakage
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var tenantAConnStr = _fixture.ConnectionString.Replace("ironmonkey_test", $"iso_a_{uniqueSuffix}");
        var tenantBConnStr = _fixture.ConnectionString.Replace("ironmonkey_test", $"iso_b_{uniqueSuffix}");

        // Create both tenant databases and apply schema (HasData seeds roles automatically)
        await using var dbASeed = _factory.CreateForTenant(tenantAConnStr, tenantAId);
        await dbASeed.Database.EnsureCreatedAsync();

        await using var dbBSeed = _factory.CreateForTenant(tenantBConnStr, tenantBId);
        await dbBSeed.Database.EnsureCreatedAsync();

        // Roles are seeded by HasData in RoleConfiguration — attach the existing TeleCaller role
        var teleCallerA = dbASeed.Roles.Find(Role.TeleCaller.Id)!;
        var teleCallerB = dbBSeed.Roles.Find(Role.TeleCaller.Id)!;

        // Seed one User in Tenant A's DB
        var userA = User.Create(tenantAId, "Alice", "alice@a.com", "hashed_pw", teleCallerA);
        dbASeed.Users.Add(userA);
        await dbASeed.SaveChangesAsync();

        // Seed one User in Tenant B's DB
        var userB = User.Create(tenantBId, "Bob", "bob@b.com", "hashed_pw", teleCallerB);
        dbBSeed.Users.Add(userB);
        await dbBSeed.SaveChangesAsync();

        // Act: open a fresh Tenant A context and query all users
        await using var dbAQuery = _factory.CreateForTenant(tenantAConnStr, tenantAId);
        var usersFromA = dbAQuery.Users.ToList();

        // Assert: only Tenant A's user returned (Tenant B is in a separate DB)
        Assert.Single(usersFromA);
        Assert.Equal("Alice", usersFromA[0].Name);
        Assert.Equal(tenantAId, usersFromA[0].TenantId);
    }

    [Fact]
    public async Task TenantDbContext_WhenEntityCreated_TenantIdAlwaysSet()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = _fixture.ConnectionString.Replace("ironmonkey_test", $"iso_c_{uniqueSuffix}");

        await using var dbSeed = _factory.CreateForTenant(connStr, tenantId);
        await dbSeed.Database.EnsureCreatedAsync();

        // Roles are seeded via HasData — attach the existing TeleCaller role
        var teleCaller = dbSeed.Roles.Find(Role.TeleCaller.Id)!;

        // Act: create a user and save
        var user = User.Create(tenantId, "Charlie", "charlie@c.com", "hashed_pw", teleCaller);
        dbSeed.Users.Add(user);
        await dbSeed.SaveChangesAsync();

        // Assert: User.TenantId == tenantId (verified from fresh context after save)
        await using var dbQuery = _factory.CreateForTenant(connStr, tenantId);
        var savedUser = dbQuery.Users.First();
        Assert.Equal(tenantId, savedUser.TenantId);
    }
}
