using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// TNCY-02: EF Core global query filters prevent cross-tenant data reads
[Collection("Integration")]
public class TenantIsolationTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public TenantIsolationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Skip = "Wave 0 stub — implement after Plan 02 (TenantDbContext with query filters)")]
    public async Task TenantDbContext_WhenTenantAContext_CannotReadTenantBData()
    {
        // Arrange: two tenant DBs, each with one User record
        // Act: query Tenant A context for all users
        // Assert: returns only Tenant A's user, not Tenant B's
        await Task.CompletedTask;
    }

    [Fact(Skip = "Wave 0 stub — implement after Plan 02 (TenantDbContext with query filters)")]
    public async Task TenantDbContext_WhenEntityCreated_TenantIdAlwaysSet()
    {
        // Arrange: TenantDbContext for tenant X
        // Act: add a User without explicitly setting TenantId (expect error or auto-set)
        // Assert: User.TenantId == X
        await Task.CompletedTask;
    }
}
