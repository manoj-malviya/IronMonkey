using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// The boundary protecting the shared recipe catalog.
///
/// IndustryRecipe lives in the central DB and is read by every tenant's signup, so the
/// write endpoints (POST/PUT/DELETE /api/recipes) are gated on admin:access rather than a
/// bare .RequireAuthorization() — which would have let any authenticated tenant user
/// rewrite the templates every other tenant provisions from.
///
/// That gate is only as good as the seed data behind it: if a tenant role ever picked up
/// admin:access, the group would silently stop being platform-only and nothing else in the
/// suite would notice. These tests pin the invariant the gate depends on.
/// </summary>
[Collection("Integration")]
public class RecipeAdminPermissionTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<string> MigrateAsync(Guid tenantId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connectionString = fixture.ConnectionString.Replace("ironmonkey_test", $"rcp_{suffix}");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    [Fact]
    public async Task TenantAdmin_IsNotGrantedAdminAccess()
    {
        var connectionString = await MigrateAsync(Guid.NewGuid());
        await using var db = _factory.CreateForTenant(connectionString, Guid.NewGuid());

        // The Admin created from signup credentials during provisioning is the most
        // privileged tenant user there is. If even that role held admin:access, gating the
        // recipe writes on it would protect nothing.
        var granted = await db.RolePermissions.AnyAsync(rp =>
            rp.RoleId == Role.Admin.Id && rp.PermissionId == Permission.AdminAccess.Id);

        Assert.False(granted,
            "A tenant Admin must never hold admin:access — it is what makes /admin/* and " +
            "the recipe write endpoints platform-only.");
    }

    [Fact]
    public async Task NoTenantRole_IsGrantedAdminAccess()
    {
        var connectionString = await MigrateAsync(Guid.NewGuid());
        await using var db = _factory.CreateForTenant(connectionString, Guid.NewGuid());

        // Broader than the Admin case: any tenant role acquiring the grant reopens the hole.
        var holders = await db.RolePermissions
            .Where(rp => rp.PermissionId == Permission.AdminAccess.Id && rp.RoleId != Role.SuperAdmin.Id)
            .Select(rp => rp.RoleId)
            .ToListAsync();

        Assert.True(holders.Count == 0,
            $"Only the platform SuperAdmin may hold admin:access; found role ids: {string.Join(", ", holders)}.");
    }

    [Fact]
    public void SuperAdmin_RetainsAdminAccess_ForPlatformUsers()
    {
        // A platform user has no role_permissions rows anywhere — their grants resolve from
        // this set instead. Gating the recipe writes on admin:access only works if the
        // platform path still supplies it.
        var granted = PermissionConstants.ForPlatformRole(RoleConstants.SuperAdmin);

        Assert.Contains(PermissionConstants.AdminAccess, granted);
    }
}
