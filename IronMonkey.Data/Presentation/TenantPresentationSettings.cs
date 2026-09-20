namespace IronMonkey.Data.Presentation;

/// <summary>
/// Everything about how a tenant's CRM *reads and looks*, as opposed to what it stores.
///
/// Persisted as a single jsonb column on the central Tenant row rather than in the tenant
/// database: the web shell needs these values on every render, including before any
/// tenant-scoped query has run, and resolving them must not require opening a second
/// connection per request.
///
/// Every member is optional. An absent section means "tenant has configured nothing here"
/// and resolves to built-in defaults, so a tenant provisioned before this existed behaves
/// exactly as it did.
/// </summary>
public sealed class TenantPresentationSettings
{
    public TenantTerminology? Terminology { get; set; }
    public TenantLocale? Locale { get; set; }
    public TenantBranding? Branding { get; set; }
}
