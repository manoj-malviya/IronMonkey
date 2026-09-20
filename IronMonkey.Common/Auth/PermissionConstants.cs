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

    /// <summary>
    /// Read a tenant's workflow execution history.
    ///
    /// Separate from settings:read because the two answer different questions: settings:read
    /// is "may this user see how automation is configured", while this is "may they see the
    /// leads it ran against" — the history carries lead names, assignees and recipient
    /// domains. Granted to Admin by default (see RolePermissions seeding), which keeps the
    /// current Admin experience unchanged while leaving the narrower grant available.
    /// </summary>
    public const string WorkflowLogsRead = "workflow:logs:read";

    /// <summary>
    /// Send a message to a lead or contact, and read the conversation.
    ///
    /// Separate from leads:write because sending is outward-facing in a way editing a record
    /// is not: a mistake reaches a customer and cannot be undone. A tenant may reasonably let
    /// a junior user edit leads while withholding the ability to email them.
    /// </summary>
    public const string MessagesSend = "messages:send";

    /// <summary>
    /// Read message history and manage templates and consent.
    ///
    /// Read is split from send because the conversation body is the most sensitive data in
    /// the CRM — it carries whatever a customer wrote — and a role that may see a lead does
    /// not automatically need to read their correspondence.
    /// </summary>
    public const string MessagesRead = "messages:read";

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
        WorkflowLogsRead,
        MessagesSend, MessagesRead,
        AdminAccess
    };
}
