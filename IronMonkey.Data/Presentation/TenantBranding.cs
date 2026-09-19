namespace IronMonkey.Data.Presentation;

/// <summary>
/// Tenant branding for the authenticated shell and tenant-facing pages.
///
/// Every value here is rendered into a page, so the schema is deliberately narrow: a display
/// name, a logo reference, and two colours. It is not a stylesheet. An open-ended CSS or
/// markup field would be a stored-XSS vector reaching every user in the tenant, and the
/// values are validated on write rather than trusted on read.
///
/// The platform console keeps the platform's own branding regardless, so an operator can
/// always tell whose data they are looking at — especially while impersonating.
/// </summary>
public sealed class TenantBranding
{
    /// <summary>Shown in the shell instead of the registered company name, when set.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Relative path or absolute https URL of the logo. Validated on write.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Hex colour, e.g. "#1d4ed8". Validated against a strict pattern on write.</summary>
    public string? PrimaryColor { get; set; }

    /// <summary>Hex colour used for accents/highlights.</summary>
    public string? AccentColor { get; set; }
}
