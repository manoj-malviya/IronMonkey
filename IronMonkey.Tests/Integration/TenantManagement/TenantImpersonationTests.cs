using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using IronMonkey.ApiService.Authentication.Services;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Cache;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.Tests.Integration.TenantManagement;

/// <summary>
/// Covers the token shape and permission resolution behind
/// <c>POST /admin/tenants/{id}/impersonate</c> — the mechanism that lets a platform operator
/// reach tenant-scoped endpoints, which otherwise throw on a Guid.Empty tenant claim.
/// </summary>
[Collection("Integration")]
public class TenantImpersonationTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public TenantImpersonationTests(PostgreSqlFixture fixture)
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

    private static Jwt CreateJwt() =>
        new(Options.Create(new JwtOptions { Key = "test-signing-key-that-is-long-enough-for-hmac-sha256" }));

    private static ICacheService PassthroughCache()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<HashSet<string>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);
        return cache.Object;
    }

    /// <summary>Provisions a real tenant and returns it with its seeded Admin user.</summary>
    private async Task<(Tenant Tenant, User AdminUser, TenantDbContextFactory Factory)>
        ProvisionTenantAsync(CentralDbContext centralDb)
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signup = SignupRequest.Create(
            companyName: $"Impersonation Co {uniqueId}",
            adminEmail: $"admin-{uniqueId}@impersonate.test",
            adminPasswordHash: BC.HashPassword("TenantPass123!"),
            phone: "555-0800",
            recipeId: null,
            companySize: "1-10",
            address: "1 Impersonation Way",
            billingContact: $"billing-{uniqueId}@impersonate.test");

        centralDb.SignupRequests.Add(signup);
        await centralDb.SaveChangesAsync();
        signup.Approve();
        await centralDb.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CentralDb"] = _fixture.ConnectionString
            })
            .Build();

        var factory = new TenantDbContextFactory();
        await new TenantProvisioningService(centralDb, factory, config)
            .ProvisionTenantAsync(signup.Id);

        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Id == signup.TenantId);

        await using var tenantDb = factory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);
        var adminUser = await tenantDb.Users
            .AsNoTracking()
            .Include(u => u.Roles)
            .SingleAsync(u => u.Email == signup.AdminEmail);

        return (tenant, adminUser, factory);
    }

    [Fact]
    public async Task ImpersonationToken_CarriesTenantScopeAndActAsClaim()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var (tenant, adminUser, _) = await ProvisionTenantAsync(centralDb);
        var platformUserId = Guid.NewGuid();

        var token = CreateJwt().GenerateToken(
            new LoggedInUser(
                adminUser.Id.ToString(),
                adminUser.Name,
                adminUser.Email,
                RoleConstants.Admin,
                tenant.Id),
            lifetime: TimeSpan.FromMinutes(30),
            actAs: platformUserId.ToString());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        // Scoped to the target tenant and to that tenant's Admin — not to the platform.
        Assert.Equal(tenant.Id.ToString(), jwt.Claims.Single(c => c.Type == "tenant_id").Value);
        Assert.Equal(
            adminUser.Id.ToString(),
            jwt.Claims.Single(c => c.Type == ClaimTypes.NameIdentifier).Value);

        // The marker that records who is really acting.
        Assert.Equal(platformUserId.ToString(), jwt.Claims.Single(c => c.Type == Jwt.ActAsClaim).Value);
    }

    [Fact]
    public async Task ImpersonationToken_ExpiresInMinutesNotYears()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var (tenant, adminUser, _) = await ProvisionTenantAsync(centralDb);

        var impersonation = CreateJwt().GenerateToken(
            new LoggedInUser(adminUser.Id.ToString(), adminUser.Name, adminUser.Email,
                RoleConstants.Admin, tenant.Id),
            lifetime: TimeSpan.FromMinutes(30),
            actAs: Guid.NewGuid().ToString());

        var expiry = new JwtSecurityTokenHandler().ReadJwtToken(impersonation).ValidTo;

        // A borrowed tenant identity must lapse on its own — the token is stateless and
        // cannot be revoked, so its lifetime is the only bound.
        Assert.True(expiry < DateTime.UtcNow.AddHours(1), $"Expiry {expiry:o} is too far out.");
        Assert.True(expiry > DateTime.UtcNow.AddMinutes(20), $"Expiry {expiry:o} is too soon.");
    }

    [Fact]
    public void OrdinaryLoginToken_IsUnchangedByTheNewParameters()
    {
        // The two existing LoginEndpoint call sites pass neither parameter and must keep
        // their long-lived, marker-free behaviour.
        var token = CreateJwt().GenerateToken(
            new LoggedInUser(Guid.NewGuid().ToString(), "Platform Admin",
                "admin@ironmonkey.local", RoleConstants.SuperAdmin, Guid.Empty));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.DoesNotContain(jwt.Claims, c => c.Type == Jwt.ActAsClaim);
        Assert.True(jwt.ValidTo > DateTime.UtcNow.AddDays(300));
    }

    [Fact]
    public async Task ImpersonatedIdentity_ResolvesTenantPermissions_ButNeverAdminAccess()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var (tenant, adminUser, factory) = await ProvisionTenantAsync(centralDb);

        var sut = new AuthorizationService(centralDb, factory, PassthroughCache());
        var permissions = await sut.GetPermissionsForUserAsync(adminUser.Id.ToString(), tenant.Id);

        // The whole point: the borrowed identity resolves the tenant's real grants through
        // the ordinary role_permissions path, with no special case in AuthorizationService.
        Assert.Contains(PermissionConstants.LeadsWrite, permissions);
        Assert.Contains(PermissionConstants.UsersWrite, permissions);

        // Critical: impersonating must never hand back platform administration. If this
        // fails, an impersonation token could approve signups or provision tenants.
        Assert.DoesNotContain(PermissionConstants.AdminAccess, permissions);
    }

    [Fact]
    public async Task AuditRow_RecordsWhoActedAsWhomAndUntilWhen()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var (tenant, adminUser, _) = await ProvisionTenantAsync(centralDb);

        var platformUserId = Guid.NewGuid();
        var expiresAt = DateTime.UtcNow.AddMinutes(30);

        centralDb.TenantImpersonations.Add(TenantImpersonation.Create(
            platformUserId: platformUserId,
            platformUserEmail: "admin@ironmonkey.local",
            tenantId: tenant.Id,
            impersonatedUserId: adminUser.Id,
            impersonatedUserEmail: adminUser.Email,
            expiresAt: expiresAt,
            reason: "support ticket 123"));
        await centralDb.SaveChangesAsync();

        var row = await centralDb.TenantImpersonations
            .AsNoTracking()
            .SingleAsync(r => r.TenantId == tenant.Id && r.PlatformUserId == platformUserId);

        Assert.Equal(adminUser.Id, row.ImpersonatedUserId);
        Assert.Equal(adminUser.Email, row.ImpersonatedUserEmail);
        Assert.Equal("support ticket 123", row.Reason);
        Assert.True(row.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task TenantRoles_AreReadableFromTheTenantDatabase()
    {
        // Covers the replacement for GET /roles, which returned 500 because it queried the
        // central DB, where the tenant-scoped Roles table does not exist.
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var (tenant, _, factory) = await ProvisionTenantAsync(centralDb);

        await using var tenantDb = factory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);
        var roles = await tenantDb.Roles
            .AsNoTracking()
            .Include(r => r.Permissions)
            .OrderBy(r => r.Id)
            .ToListAsync();

        Assert.Contains(roles, r => r.Name == RoleConstants.Admin);

        var admin = roles.Single(r => r.Name == RoleConstants.Admin);
        Assert.NotEmpty(admin.Permissions);
        Assert.DoesNotContain(admin.Permissions, p => p.Name == PermissionConstants.AdminAccess);
    }
}
