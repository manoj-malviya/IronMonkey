namespace IronMonkey.Common.Auth;

/// <summary>
/// Permission names as used by [HasPermission] policies.
/// Kept in sync with the seeded rows in IronMonkey.Data PermissionConfiguration.
/// </summary>
public static class PermissionConstants
{
    public const string UsersRead = "users:read";
    public const string UsersWrite = "users:write";
    public const string UsersDelete = "users:delete";
    public const string LeadsRead = "leads:read";
    public const string LeadsWrite = "leads:write";
    public const string LeadsDelete = "leads:delete";
    public const string ContactsRead = "contacts:read";
    public const string ContactsWrite = "contacts:write";
    public const string ContactsDelete = "contacts:delete";
    public const string OpportunitiesRead = "opportunities:read";
    public const string OpportunitiesWrite = "opportunities:write";
    public const string OpportunitiesDelete = "opportunities:delete";
    public const string ReportsRead = "reports:read";
    public const string ReportsWrite = "reports:write";
    public const string SettingsRead = "settings:read";
    public const string SettingsWrite = "settings:write";

    /// <summary>Platform administration: approve/reject signups, provision tenants.</summary>
    public const string AdminAccess = "admin:access";

    /// <summary>
    /// Permissions granted to a platform role. Platform users live in the central DB and
    /// have no rows in a tenant's role_permissions table, so their grants are resolved here.
    /// </summary>
    public static IReadOnlySet<string> ForPlatformRole(string role) => role switch
    {
        RoleConstants.SuperAdmin => SuperAdminPermissions,
        _ => new HashSet<string>()
    };

    private static readonly IReadOnlySet<string> SuperAdminPermissions = new HashSet<string>
    {
        UsersRead, UsersWrite, UsersDelete,
        LeadsRead, LeadsWrite, LeadsDelete,
        ContactsRead, ContactsWrite, ContactsDelete,
        OpportunitiesRead, OpportunitiesWrite, OpportunitiesDelete,
        ReportsRead, ReportsWrite,
        SettingsRead, SettingsWrite,
        AdminAccess
    };
}
