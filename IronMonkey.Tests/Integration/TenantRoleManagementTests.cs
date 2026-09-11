using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// The tenant/platform boundary: a tenant must never see, assign or grant SuperAdmin or
/// admin:access. These assert the rules in <see cref="TenantRoleRules"/> against real seeded
/// data, so a change to the seed that reintroduces either is caught here.
/// </summary>
[Collection("Integration")]
public class TenantRoleManagementTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<(string ConnectionString, Guid TenantId)> SetupAsync()
    {
        var tenantId = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"rm_{suffix}");

        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();
        return (connStr, tenantId);
    }

    [Fact]
    public async Task SuperAdmin_is_hidden_from_tenants_but_still_seeded()
    {
        var (connStr, tenantId) = await SetupAsync();
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var all = await db.Roles.AsNoTracking().Select(r => r.Id).ToListAsync();
        Assert.Contains(1, all); // the platform role still exists in the tenant DB

        var visible = all.Where(TenantRoleRules.IsVisibleToTenant).ToList();
        Assert.DoesNotContain(1, visible);
        Assert.Contains(201, visible); // Admin remains visible
    }

    [Fact]
    public async Task Admin_access_is_never_grantable_by_a_tenant()
    {
        var (connStr, tenantId) = await SetupAsync();
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var grantable = await db.Set<Permission>().AsNoTracking().Select(p => p.Name).ToListAsync();

        Assert.Contains(PermissionConstants.AdminAccess, grantable);
        Assert.False(TenantRoleRules.IsGrantableByTenant(PermissionConstants.AdminAccess));
        Assert.True(TenantRoleRules.IsGrantableByTenant(PermissionConstants.LeadsWrite));
    }

    [Fact]
    public async Task Seeded_roles_are_protected_and_custom_ids_never_collide()
    {
        var (connStr, tenantId) = await SetupAsync();
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var seeded = await db.Roles.AsNoTracking().Select(r => r.Id).ToListAsync();

        Assert.All(seeded, id => Assert.True(TenantRoleRules.IsSystemRole(id)));
        // Every seeded id sits below the floor custom roles are allocated from.
        Assert.All(seeded, id => Assert.True(id < TenantRoleRules.CustomRoleIdFloor));
    }

    [Fact]
    public async Task A_custom_role_can_hold_permissions()
    {
        var (connStr, tenantId) = await SetupAsync();

        await using (var db = _factory.CreateForTenant(connStr, tenantId))
        {
            var role = Role.Create(TenantRoleRules.CustomRoleIdFloor, "Sales Manager");
            var leadsWrite = await db.Set<Permission>().SingleAsync(p => p.Name == PermissionConstants.LeadsWrite);
            role.AddPermission(leadsWrite);
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateForTenant(connStr, tenantId);
        var saved = await verify.Roles
            .Include(r => r.Permissions)
            .SingleAsync(r => r.Id == TenantRoleRules.CustomRoleIdFloor);

        Assert.Equal("Sales Manager", saved.Name);
        Assert.Equal(PermissionConstants.LeadsWrite, Assert.Single(saved.Permissions).Name);
        Assert.False(TenantRoleRules.IsSystemRole(saved.Id));
    }
}
