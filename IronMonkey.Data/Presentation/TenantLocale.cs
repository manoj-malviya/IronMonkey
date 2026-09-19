namespace IronMonkey.Data.Presentation;

/// <summary>
/// How this tenant reads numbers, money and dates.
///
/// The app runs under the invariant culture, whose currency symbol is the placeholder U+00A4 —
/// which is why money has been rendered as a bare number so far. Currency is a tenant fact,
/// not a server fact, so it is stored here rather than inferred from the process culture.
///
/// Timestamps stay UTC in the database. This only changes how they are rendered: rewriting
/// stored instants would corrupt existing rows and break the half-open date-range arithmetic
/// the dashboard depends on.
/// </summary>
public sealed class TenantLocale
{
    /// <summary>ISO 4217 code, e.g. "GBP". Null means "not configured" — never guess.</summary>
    public string? CurrencyCode { get; set; }

    /// <summary>
    /// Symbol to render, e.g. "£". Stored explicitly rather than looked up from the code,
    /// because the invariant culture cannot resolve one and tenants sharing a code do not
    /// always want the same glyph ("$" vs "US$").
    /// </summary>
    public string? CurrencySymbol { get; set; }

    /// <summary>BCP 47 tag, e.g. "en-GB", used for number and date formatting.</summary>
    public string? Culture { get; set; }

    /// <summary>
    /// IANA zone id, e.g. "Europe/London". IANA rather than a Windows id because the
    /// deployment is Linux and PostgreSQL speaks IANA.
    /// </summary>
    public string? TimeZoneId { get; set; }
}
