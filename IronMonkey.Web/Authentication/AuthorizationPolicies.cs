namespace IronMonkey.Web.Authentication;

/// <summary>
/// Authorization policy names used by [Authorize(Policy = ...)] on Razor pages.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>
    /// Platform operator only (SuperAdmin). Gates pages that administer tenants rather
    /// than pages a tenant's own Admin uses — the API mirrors this with the
    /// admin:access permission on its /admin/* group.
    /// </summary>
    public const string PlatformAdmin = "PlatformAdmin";
}
