using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// The permission boundary around messaging.
///
/// Sending is outward-facing in a way editing a record is not: a mistake reaches a customer
/// and cannot be recalled. Reading is separate again, because a message body carries whatever
/// the customer wrote and is the most sensitive data in the CRM.
///
/// Both are gated on their own permissions rather than folded into leads:* — but a separate
/// permission only means something if the seed keeps it separate, which is what these pin. A
/// later seed change could quietly grant them to every role with nothing else failing.
/// </summary>
[Collection("Integration")]
public class MessagingPermissionTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<TenantDbContext> MigrateAsync()
    {
        var tenantId = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var db = _factory.CreateForTenant(
            fixture.ConnectionString.Replace("ironmonkey_test", $"msgperm_{suffix}"), tenantId);

        await db.Database.MigrateAsync();
        return db;
    }

    [Fact]
    public async Task The_messaging_permissions_are_seeded()
    {
        await using var db = await MigrateAsync();

        var names = await db.Permissions
            .Where(p => p.Id == Permission.MessagesSend.Id || p.Id == Permission.MessagesRead.Id)
            .Select(p => p.Name)
            .ToListAsync();

        Assert.Contains(PermissionConstants.MessagesSend, names);
        Assert.Contains(PermissionConstants.MessagesRead, names);
    }

    [Fact]
    public async Task A_tenant_Admin_may_send_and_read_messages()
    {
        await using var db = await MigrateAsync();

        Assert.True(await db.RolePermissions.AnyAsync(rp =>
            rp.RoleId == Role.Admin.Id && rp.PermissionId == Permission.MessagesSend.Id));

        Assert.True(await db.RolePermissions.AnyAsync(rp =>
            rp.RoleId == Role.Admin.Id && rp.PermissionId == Permission.MessagesRead.Id));
    }

    [Fact]
    public async Task Messaging_is_not_granted_to_every_role_by_default()
    {
        // Owner and TeleCaller do not get messaging from the seed. The grant is deliberately
        // narrow: a role that may view leads should not automatically be able to message them.
        await using var db = await MigrateAsync();

        foreach (var roleId in new[] { Role.Owner.Id, Role.TeleCaller.Id })
        {
            Assert.False(await db.RolePermissions.AnyAsync(rp =>
                rp.RoleId == roleId && rp.PermissionId == Permission.MessagesSend.Id),
                $"Role {roleId} must not hold messages:send by default — sending reaches customers.");
        }
    }

    [Fact]
    public async Task Messaging_permissions_are_distinct_from_leads_write()
    {
        // If messages:send were merely an alias for leads:write, the separate permission would
        // be decoration. This pins that they are genuinely different ids.
        await using var db = await MigrateAsync();

        Assert.NotEqual(Permission.LeadsWrite.Id, Permission.MessagesSend.Id);
        Assert.NotEqual(Permission.LeadsRead.Id, Permission.MessagesRead.Id);

        var distinct = await db.Permissions.Select(p => p.Id).ToListAsync();
        Assert.Equal(distinct.Count, distinct.Distinct().Count());
    }

    [Fact]
    public void A_platform_SuperAdmin_resolves_messaging_permissions()
    {
        // Platform users have no role_permissions rows, so their grants come from this set.
        var granted = PermissionConstants.ForPlatformRole(RoleConstants.SuperAdmin);

        Assert.Contains(PermissionConstants.MessagesSend, granted);
        Assert.Contains(PermissionConstants.MessagesRead, granted);
    }
}
