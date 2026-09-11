namespace IronMonkey.Common.Auth;

/// <summary>
/// What a tenant is allowed to see and change in its own role catalogue.
///
/// SuperAdmin and admin:access are the boundary between a tenant and the platform: a tenant
/// that could grant either would be able to reach /admin/* and administer other tenants. Both
/// are therefore filtered out of every tenant-facing read and rejected on every write, rather
/// than merely hidden in the UI.
/// </summary>
public static class TenantRoleRules
{
    /// <summary>Roles a tenant must never see, edit, assign or delete.</summary>
    public static readonly IReadOnlySet<int> HiddenRoleIds = new HashSet<int> { 1 };

    /// <summary>Permissions a tenant must never see or grant.</summary>
    public static readonly IReadOnlySet<string> HiddenPermissions =
        new HashSet<string> { PermissionConstants.AdminAccess };

    /// <summary>
    /// Seeded roles. Their grants may be edited, but they cannot be renamed or deleted:
    /// provisioning assigns Admin to the signup user, and the others are referenced by
    /// name in recipes and seed data.
    /// </summary>
    public static readonly IReadOnlySet<int> SystemRoleIds = new HashSet<int> { 1, 201, 301, 302 };

    /// <summary>Custom roles start above the seeded band so they never collide with it.</summary>
    public const int CustomRoleIdFloor = 1000;

    public static bool IsVisibleToTenant(int roleId) => !HiddenRoleIds.Contains(roleId);

    public static bool IsGrantableByTenant(string permissionName) =>
        !HiddenPermissions.Contains(permissionName);

    /// <summary>A seeded role keeps its name and cannot be removed.</summary>
    public static bool IsSystemRole(int roleId) => SystemRoleIds.Contains(roleId);
}
