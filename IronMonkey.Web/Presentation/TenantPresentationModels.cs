using IronMonkey.Data.Presentation;

namespace IronMonkey.Web.Presentation;

/// <summary>Wire shape returned by GET /api/tenant/presentation.</summary>
public sealed record TenantPresentationResponse(
    Dictionary<string, TermResponse>? Terms,
    TenantTerminology? Overrides,
    LocaleResponse? Locale,
    TenantBranding? Branding,
    string? TenantName);

public sealed record TermResponse(string Singular, string Plural);

public sealed record LocaleResponse(
    string? CurrencyCode,
    string? CurrencySymbol,
    string? Culture,
    string? TimeZoneId,
    string? TimeZoneLabel);

public sealed record UpdateTenantPresentationRequest(
    TenantTerminology? Terminology,
    TenantLocale? Locale,
    TenantBranding? Branding);
