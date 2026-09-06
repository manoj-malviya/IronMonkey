using IronMonkey.ApiService.Authentication.Endpoints;
using IronMonkey.ApiService.Authentication.Services;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Cache;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.Tests.Integration.TenantManagement;

[Collection("Integration")]
public class PlatformAdminTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PlatformAdminTests(PostgreSqlFixture fixture)
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

    private static PlatformAdminSeeder CreateSeeder(CentralDbContext db, string? email, string? password)
        => new(
            db,
            Options.Create(new PlatformAdminOptions { Email = email, Password = password }),
            NullLogger<PlatformAdminSeeder>.Instance);

    /// <summary>A cache that always misses, so tests exercise the real lookup.</summary>
    private static ICacheService PassthroughCache()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<HashSet<string>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);
        return cache.Object;
    }

    [Fact]
    public async Task Seeder_WhenConfigured_CreatesSuperAdmin()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var email = $"platform-{Guid.NewGuid():N}@ironmonkey.local";
        await CreateSeeder(centralDb, email, "SeedPass123!").SeedAsync();

        var seeded = await centralDb.PlatformUsers.SingleOrDefaultAsync(u => u.Email == email);

        Assert.NotNull(seeded);
        Assert.Equal(RoleConstants.SuperAdmin, seeded!.Role);
        Assert.True(BC.Verify("SeedPass123!", seeded.PasswordHash));
    }

    [Fact]
    public async Task Seeder_WithoutPassword_SeedsNothing()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var email = $"platform-{Guid.NewGuid():N}@ironmonkey.local";
        await CreateSeeder(centralDb, email, password: null).SeedAsync();

        Assert.False(await centralDb.PlatformUsers.AnyAsync(u => u.Email == email));
    }

    [Fact]
    public async Task Seeder_RunTwice_DoesNotOverwriteRotatedPassword()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var email = $"platform-{Guid.NewGuid():N}@ironmonkey.local";
        await CreateSeeder(centralDb, email, "OriginalPass123!").SeedAsync();

        // Operator rotates the password out of band.
        var user = await centralDb.PlatformUsers.SingleAsync(u => u.Email == email);
        user.ResetPassword(BC.HashPassword("RotatedPass456!"));
        await centralDb.SaveChangesAsync();

        // A restart re-runs the seeder with the original configured password.
        await CreateSeeder(centralDb, email, "OriginalPass123!").SeedAsync();

        var after = await centralDb.PlatformUsers.SingleAsync(u => u.Email == email);
        Assert.True(BC.Verify("RotatedPass456!", after.PasswordHash));
        Assert.Single(await centralDb.PlatformUsers.Where(u => u.Email == email).ToListAsync());
    }

    [Fact]
    public async Task Login_AsPlatformAdmin_ReturnsTokenWithEmptyTenant()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var email = $"platform-{Guid.NewGuid():N}@ironmonkey.local";
        await CreateSeeder(centralDb, email, "LoginPass123!").SeedAsync();

        var jwt = new Jwt(Options.Create(new JwtOptions { Key = new string('k', 64) }));

        var result = await LoginEndpoint.HandleForTest(
            new LoginEndpoint.Request(email, "LoginPass123!"),
            centralDb,
            new TenantDbContextFactory(),
            jwt,
            CancellationToken.None);

        var ok = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<LoginEndpoint.Response>>(result.Result);

        // A platform operator administers tenants rather than belonging to one.
        Assert.Equal(Guid.Empty, ok.Value!.TenantId);

        var principal = jwt.GetUserFromToken(ok.Value.Token);
        Assert.Equal(RoleConstants.SuperAdmin, principal.Role);
        Assert.Equal(Guid.Empty, principal.TenantId);
    }

    [Fact]
    public async Task Login_AsPlatformAdmin_WithWrongPassword_IsUnauthorized()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var email = $"platform-{Guid.NewGuid():N}@ironmonkey.local";
        await CreateSeeder(centralDb, email, "RightPass123!").SeedAsync();

        var jwt = new Jwt(Options.Create(new JwtOptions { Key = new string('k', 64) }));

        var result = await LoginEndpoint.HandleForTest(
            new LoginEndpoint.Request(email, "WrongPass123!"),
            centralDb,
            new TenantDbContextFactory(),
            jwt,
            CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult>(result.Result);
    }

    [Fact]
    public async Task Permissions_ForSeededSuperAdmin_IncludeAdminAccess()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var email = $"platform-{Guid.NewGuid():N}@ironmonkey.local";
        await CreateSeeder(centralDb, email, "PermPass123!").SeedAsync();
        var user = await centralDb.PlatformUsers.SingleAsync(u => u.Email == email);

        var sut = new AuthorizationService(centralDb, new TenantDbContextFactory(), PassthroughCache());

        // Guid.Empty tenant == platform operator.
        var permissions = await sut.GetPermissionsForUserAsync(user.Id.ToString(), Guid.Empty);

        Assert.Contains(PermissionConstants.AdminAccess, permissions);
        Assert.Contains(PermissionConstants.UsersDelete, permissions);
    }

    [Fact]
    public async Task Permissions_ForProvisionedTenantAdmin_AreScopedBelowPlatformAdmin()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signup = SignupRequest.Create(
            companyName: $"Perm Scope Co {uniqueId}",
            adminEmail: $"admin-{uniqueId}@permscope.com",
            adminPasswordHash: BC.HashPassword("TenantPass123!"),
            phone: "555-0700",
            recipeId: null,
            companySize: "1-10",
            address: "9 Scope St",
            billingContact: $"billing-{uniqueId}@permscope.com");
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
        var adminUser = await tenantDb.Users.AsNoTracking().SingleAsync(u => u.Email == signup.AdminEmail);

        var sut = new AuthorizationService(centralDb, factory, PassthroughCache());
        var permissions = await sut.GetPermissionsForUserAsync(adminUser.Id.ToString(), tenant.Id);

        // Resolved from the tenant DB's role_permissions rows, not the platform map.
        Assert.Contains(PermissionConstants.LeadsWrite, permissions);
        Assert.Contains(PermissionConstants.UsersRead, permissions);

        // A tenant admin must not be able to approve signups or provision tenants.
        Assert.DoesNotContain(PermissionConstants.AdminAccess, permissions);
        Assert.DoesNotContain(PermissionConstants.UsersDelete, permissions);

        // A second, lower-privilege user in the SAME tenant must not inherit the admin's
        // grants. Every tenant user has IdentityId = "" (nothing populates it during
        // provisioning), so keying authorization on that column would union both users'
        // permissions and silently escalate this caller to Admin.
        var teleCallerRole = await tenantDb.Roles.SingleAsync(r => r.Name == RoleConstants.TeleCaller);
        await using (var writeDb = factory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id))
        {
            var role = await writeDb.Roles.SingleAsync(r => r.Id == teleCallerRole.Id);
            writeDb.Users.Add(User.Create(
                tenant.Id,
                "Tele Caller",
                $"tele-{uniqueId}@permscope.com",
                BC.HashPassword("TelePass123!"),
                role));
            await writeDb.SaveChangesAsync();
        }

        await using var readDb = factory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);
        var telecaller = await readDb.Users.AsNoTracking()
            .SingleAsync(u => u.Email == $"tele-{uniqueId}@permscope.com");

        var telePermissions = await sut.GetPermissionsForUserAsync(telecaller.Id.ToString(), tenant.Id);

        Assert.DoesNotContain(PermissionConstants.LeadsWrite, telePermissions);
        Assert.DoesNotContain(PermissionConstants.UsersRead, telePermissions);
    }

    [Fact]
    public async Task Permissions_ForUnknownPlatformIdentity_AreEmpty()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var sut = new AuthorizationService(centralDb, new TenantDbContextFactory(), PassthroughCache());

        // A stranger must not be granted admin:access simply by presenting a valid token.
        var permissions = await sut.GetPermissionsForUserAsync(Guid.NewGuid().ToString(), Guid.Empty);

        Assert.Empty(permissions);
    }
}
