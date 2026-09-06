namespace IronMonkey.Common.Auth;

/// <summary>
/// Role names as they appear in the JWT role claim.
/// Kept in sync with the seeded rows in IronMonkey.Data RoleConfiguration.
/// </summary>
public static class RoleConstants
{
    /// <summary>Platform operator. Lives in the central DB, belongs to no tenant.</summary>
    public const string SuperAdmin = "SuperAdmin";

    /// <summary>Tenant administrator. Created per tenant during provisioning.</summary>
    public const string Admin = "Admin";

    public const string Owner = "Owner";
    public const string TeleCaller = "TeleCaller";
}
