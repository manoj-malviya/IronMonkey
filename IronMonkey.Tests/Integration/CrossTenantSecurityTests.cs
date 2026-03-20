using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// TNCY-02: JWT tenant claim enforced at API layer — no cross-tenant reads via HTTP
[Collection("Integration")]
public class CrossTenantSecurityTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public CrossTenantSecurityTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Skip = "Wave 0 stub — implement after Plan 03 (TenantResolutionMiddleware + JWT TenantId claim)")]
    public async Task ApiRequest_WithTenantAJwt_CannotReadTenantBData()
    {
        // Arrange: two provisioned tenants, each with a user and data
        // Act: GET /users with Tenant A's JWT
        // Assert: response contains only Tenant A users
        await Task.CompletedTask;
    }

    [Fact(Skip = "Wave 0 stub — implement after Plan 03 (TenantResolutionMiddleware + JWT TenantId claim)")]
    public async Task ApiRequest_WithNoTenantIdInJwt_Returns401OrForbidden()
    {
        // Arrange: JWT without tenant_id claim
        // Act: call tenant-required endpoint
        // Assert: 401 Unauthorized or 403 Forbidden
        await Task.CompletedTask;
    }
}
