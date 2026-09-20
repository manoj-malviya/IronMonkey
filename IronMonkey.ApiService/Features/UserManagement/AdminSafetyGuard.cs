using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.UserManagement;

/// <summary>
/// Stops a tenant locking itself out of its own administration.
///
/// A tenant with no active Admin cannot invite anyone, cannot change a role and cannot get
/// its Admin back without platform intervention — so removing the last one is refused at the
/// API, not merely hidden in the UI. Every path that could take an Admin away goes through
/// here: deactivation, a role change off Admin, and (for completeness) anything else that
/// ends an Admin's membership.
/// </summary>
public static class AdminSafetyGuard
{
    /// <summary>Seeded tenant Admin. SuperAdmin (1) is platform-only and never counts.</summary>
    public const int AdminRoleId = 201;

    public const string LastAdminMessage =
        "This is the last active Admin. Promote another user to Admin first.";

    /// <summary>
    /// Counts active Admins, optionally excluding one user.
    ///
    /// Queried from Roles and traversed through Role.Users, never
    /// <c>db.Users.Where(u =&gt; u.Roles...)</c>: User.Roles is a computed property
    /// (<c>=&gt; _roles.ToList()</c>) that EF cannot translate, so the obvious spelling
    /// throws at runtime. Role.Users is the real mapped navigation.
    ///
    /// The User query filter already excludes soft-deleted rows, so "active" is implicit —
    /// but IgnoreQueryFilters is deliberately NOT used here: a deactivated Admin is exactly
    /// what must not be counted as cover.
    /// </summary>
    public static async Task<int> CountActiveAdminsAsync(
        TenantDbContext db,
        Guid tenantId,
        Guid? excludingUserId,
        CancellationToken cancellationToken)
        => await db.Roles
            .Where(r => r.Id == AdminRoleId)
            .SelectMany(r => r.Users)
            .Where(u => u.TenantId == tenantId && !u.IsDeleted)
            .Where(u => excludingUserId == null || u.Id != excludingUserId)
            .CountAsync(cancellationToken);

    /// <summary>
    /// True when removing <paramref name="userId"/>'s Admin standing would leave none.
    ///
    /// Returns false when the user is not an active Admin at all — deactivating a TeleCaller
    /// is never blocked by this guard, whatever the Admin count is.
    /// </summary>
    public static async Task<bool> WouldRemoveLastAdminAsync(
        TenantDbContext db,
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var isAdmin = await db.Roles
            .Where(r => r.Id == AdminRoleId)
            .SelectMany(r => r.Users)
            .AnyAsync(u => u.TenantId == tenantId && !u.IsDeleted && u.Id == userId, cancellationToken);

        if (!isAdmin)
            return false;

        var remaining = await CountActiveAdminsAsync(db, tenantId, excludingUserId: userId, cancellationToken);
        return remaining == 0;
    }
}
