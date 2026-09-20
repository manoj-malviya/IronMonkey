using IronMonkey.Data.Presentation;

namespace IronMonkey.ApiService.Features.Presentation;

/// <summary>
/// Wire shape for a tenant's presentation settings.
///
/// The terminology map is sent as fully-resolved values rather than as sparse overrides: the
/// client renders labels, and making it re-implement the fallback chain is how a blank label
/// eventually ships. <see cref="Overrides"/> carries the sparse form for the settings editor,
/// which does need to know what the tenant actually set.
/// </summary>
public sealed record TenantPresentationResponse(
    Dictionary<string, TermResponse> Terms,
    TenantTerminology Overrides,
    LocaleResponse Locale,
    TenantBranding Branding,
    string TenantName);

public sealed record TermResponse(string Singular, string Plural);

public sealed record LocaleResponse(
    string? CurrencyCode,
    string? CurrencySymbol,
    string? Culture,
    string? TimeZoneId,
    string TimeZoneLabel);

public sealed record UpdateTenantPresentationRequest(
    TenantTerminology? Terminology,
    TenantLocale? Locale,
    TenantBranding? Branding);

public static class PresentationMapping
{
    public static TenantPresentationResponse ToResponse(
        TenantPresentationSettings? settings, string tenantName)
    {
        var resolved = TenantPresentation.From(settings);

        var terms = TerminologyDefaults.All.ToDictionary(
            term => term.ToString(),
            term => new TermResponse(
                resolved.Terminology.Singular(term),
                resolved.Terminology.Plural(term)));

        var locale = settings?.Locale;

        return new TenantPresentationResponse(
            terms,
            settings?.Terminology ?? new TenantTerminology(),
            new LocaleResponse(
                locale?.CurrencyCode,
                locale?.CurrencySymbol,
                locale?.Culture,
                locale?.TimeZoneId,
                resolved.Formatting.TimeZoneLabel),
            resolved.Branding,
            tenantName);
    }
}
