using System.Security.Claims;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
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

    [Fact]
    public void TenantService_GetCurrentTenantId_ThrowsWhenNoTenantIdClaim()
    {
        // Arrange: ClaimsPrincipal without tenant_id claim
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Email, "user@example.com")
            // Deliberately no "tenant_id" claim
        ], "TestAuth");

        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };

        var httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        var options = new DbContextOptionsBuilder<CentralDbContext>()
            .UseInMemoryDatabase("TestDb_NoTenantClaim")
            .Options;
        using var centralDb = new CentralDbContext(options);

        var tenantService = new TenantService(httpContextAccessorMock.Object, centralDb);

        // Act & Assert: missing tenant_id claim must throw
        var ex = Assert.Throws<ApplicationException>(() => tenantService.GetCurrentTenantId());
        Assert.Contains("tenant_id", ex.Message);
    }

    [Fact]
    public void TenantService_GetCurrentTenantId_ReturnsTenantIdFromClaim()
    {
        // Arrange: ClaimsPrincipal with valid tenant_id claim
        var expectedTenantId = Guid.NewGuid();
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("tenant_id", expectedTenantId.ToString())
        ], "TestAuth");

        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };

        var httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        var options = new DbContextOptionsBuilder<CentralDbContext>()
            .UseInMemoryDatabase("TestDb_ValidTenantClaim")
            .Options;
        using var centralDb = new CentralDbContext(options);

        var tenantService = new TenantService(httpContextAccessorMock.Object, centralDb);

        // Act
        var tenantId = tenantService.GetCurrentTenantId();

        // Assert
        Assert.Equal(expectedTenantId, tenantId);
    }
}
