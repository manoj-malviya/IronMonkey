using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// The permission gating workflow run history.
///
/// Worth pinning because the grant is split across three places that must agree — the
/// constant, the seeded permission row, and the seeded role_permissions rows — and a
/// mismatch produces a 403 for every Admin with nothing in the logs to explain it.
/// </summary>
[Collection("Integration")]
public class WorkflowLogPermissionTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<string> MigrateAsync(Guid tenantId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connectionString = fixture.ConnectionString.Replace("ironmonkey_test", $"wfp_{suffix}");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    [Fact]
    public async Task Migration_SeedsTheWorkflowLogsPermission()
    {
        var connectionString = await MigrateAsync(Guid.NewGuid());
        await using var db = _factory.CreateForTenant(connectionString, Guid.NewGuid());

        var permission = await db.Permissions
            .SingleOrDefaultAsync(p => p.Name == PermissionConstants.WorkflowLogsRead);

        Assert.NotNull(permission);
        Assert.Equal(Permission.WorkflowLogsRead.Id, permission!.Id);
    }

    [Fact]
    public async Task TenantAdmin_IsGrantedWorkflowLogsRead()
    {
        var connectionString = await MigrateAsync(Guid.NewGuid());
        await using var db = _factory.CreateForTenant(connectionString, Guid.NewGuid());

        // Default Admin access is explicit, not inherited from settings:read — so it is
        // asserted rather than assumed.
        var granted = await db.RolePermissions.AnyAsync(rp =>
            rp.RoleId == Role.Admin.Id && rp.PermissionId == Permission.WorkflowLogsRead.Id);

        Assert.True(granted, "The tenant Admin role must be granted workflow:logs:read by default.");
    }

    [Fact]
    public async Task OwnerRole_IsNotGrantedWorkflowLogsRead()
    {
        var connectionString = await MigrateAsync(Guid.NewGuid());
        await using var db = _factory.CreateForTenant(connectionString, Guid.NewGuid());

        // The permission is separate precisely so it can be withheld. Owner is read-only on
        // users and has no business reading the leads automation ran against.
        var granted = await db.RolePermissions.AnyAsync(rp =>
            rp.RoleId == Role.Owner.Id && rp.PermissionId == Permission.WorkflowLogsRead.Id);

        Assert.False(granted);
    }

    [Fact]
    public void PlatformSuperAdmin_CarriesWorkflowLogsRead()
    {
        // Platform users resolve grants from this set rather than from role_permissions rows,
        // since they have no row in any tenant database.
        Assert.Contains(PermissionConstants.WorkflowLogsRead,
            PermissionConstants.ForPlatformRole(RoleConstants.SuperAdmin));
    }

    [Fact]
    public void NonSuperAdminPlatformRole_CarriesNothing()
    {
        Assert.Empty(PermissionConstants.ForPlatformRole("SomeOtherRole"));
    }
}
