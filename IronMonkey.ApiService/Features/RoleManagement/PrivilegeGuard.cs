using Microsoft.EntityFrameworkCore;
using IronMonkey.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.RoleManagement;

/// <summary>
/// Stops a user granting more than they hold.
///
/// <para>Without it, anyone able to manage roles could create a role carrying every
/// permission in the catalog and assign it to themselves — or edit the role they already
/// hold. The rule is the usual one: <b>a permission can only be granted, and a role only
/// assigned, by someone who already holds every permission involved.</b> <c>admin:access</c>
/// is additionally ungrantable by anyone in a tenant (see <see cref="TenantRoleRules"/>).</para>
///
/// <para>The actor's permissions are read from the same tenant database, as the union of
/// their roles' grants — the same source <c>AuthorizationService</c> resolves from — rather
/// than from the five-minute permission cache, so a grant revoked a moment ago cannot be used
/// to re-grant itself.</para>
/// </summary>
public static class PrivilegeGuard
{
    /// <summary>Permissions every Admin role must keep, or the tenant can no longer manage users.</summary>
    public static readonly IReadOnlySet<string> AdminRoleFloor =
        new HashSet<string> { PermissionConstants.UsersRead, PermissionConstants.UsersWrite };

    public static async Task<HashSet<string>> ActorPermissionsAsync(TenantDbContext db, Guid actorUserId, CancellationToken ct)
    {
        // Query from Roles: User.Roles is a computed property and cannot be translated.
        var names = await db.Roles
            .Where(r => r.Users.Any(u => u.Id == actorUserId))
            .SelectMany(r => r.Permissions)
            .Select(p => p.Name)
            .Distinct()
            .ToListAsync(ct);
        return names.ToHashSet();
    }

    /// <summary>Returns an error when <paramref name="requested"/> includes anything the actor lacks.</summary>
    public static async Task<string?> CheckCanGrantAsync(TenantDbContext db, Guid actorUserId,
        IEnumerable<string> requested, CancellationToken ct)
    {
        var held = await ActorPermissionsAsync(db, actorUserId, ct);
        var missing = requested.Where(p => !held.Contains(p)).OrderBy(p => p).ToList();

        return missing.Count == 0
            ? null
            : $"You cannot grant permissions you do not hold yourself: {string.Join(", ", missing)}.";
    }

    /// <summary>Returns an error when assigning <paramref name="roleId"/> would grant the target
    /// anything the actor lacks.</summary>
    public static async Task<string?> CheckCanAssignRoleAsync(TenantDbContext db, Guid actorUserId, int roleId, CancellationToken ct)
    {
        var rolePermissions = await db.Roles
            .Where(r => r.Id == roleId)
            .SelectMany(r => r.Permissions)
            .Select(p => p.Name)
            .ToListAsync(ct);

        var error = await CheckCanGrantAsync(db, actorUserId, rolePermissions, ct);
        return error is null ? null : "You cannot assign a role that holds permissions you do not hold yourself.";
    }
}
