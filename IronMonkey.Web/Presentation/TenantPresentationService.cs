using IronMonkey.Data.Presentation;
using IronMonkey.Web.HttpHandlers;

namespace IronMonkey.Web.Presentation;

/// <summary>
/// The single place the UI asks what to call things and how to format them.
///
/// Scoped, so it is resolved once per Blazor circuit and the API is called once rather than
/// on every render. Components inject this and read <see cref="Term"/> / <see cref="Money"/>;
/// no component holds a hardcoded noun or a currency format of its own.
///
/// Before the first load — during prerender, where the JWT in ProtectedSessionStorage is
/// unreadable — every accessor returns built-in defaults. That is the same shape the page
/// renders for a tenant that has configured nothing, so the pre-load render is never blank
/// or wrong, only unbranded.
/// </summary>
public sealed class TenantPresentationService(
    AdminApiClient apiClient,
    ILogger<TenantPresentationService> logger)
{
    private TenantPresentation _presentation = TenantPresentation.Default;
    private TenantTerminology _overrides = new();
    private LocaleResponse? _locale;
    private string? _tenantName;
    private bool _loaded;

    /// <summary>Raised once the real settings arrive, so the shell can re-render.</summary>
    public event Action? Changed;

    public ResolvedTerminology Terminology => _presentation.Terminology;
    public TenantFormatting Formatting => _presentation.Formatting;
    public TenantBranding Branding => _presentation.Branding;

    /// <summary>The sparse overrides, for the settings editor. Defaults are not included.</summary>
    public TenantTerminology Overrides => _overrides;

    /// <summary>The stored locale as configured, for the settings editor.</summary>
    public LocaleResponse? Locale => _locale;

    /// <summary>Branding display name if set, otherwise the registered tenant name.</summary>
    public string? DisplayName =>
        string.IsNullOrWhiteSpace(_presentation.Branding.DisplayName)
            ? _tenantName
            : _presentation.Branding.DisplayName;

    public bool IsLoaded => _loaded;

    /// <summary>
    /// Loads the tenant's settings once per circuit. Safe to call from every page's
    /// OnAfterRenderAsync: subsequent calls are no-ops.
    /// </summary>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
            return;

        await ReloadAsync(cancellationToken);
    }

    /// <summary>
    /// Re-fetches from the API. Called after the settings page saves, so the shell reflects
    /// a rename immediately rather than on the next login.
    /// </summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await apiClient.GetFromJsonAsync<TenantPresentationResponse>(
                "/api/tenant/presentation", cancellationToken);

            // Null here means prerender (no token yet) or a non-success status. Either way the
            // defaults already in place are the right thing to keep rendering, and _loaded
            // stays false so the next render retries — the same pattern the auth provider uses.
            if (response is null)
                return;

            Apply(response);
        }
        catch (Exception ex)
        {
            // Labels must never take a page down. Log and keep the defaults.
            logger.LogWarning(ex, "Could not load tenant presentation settings; using defaults.");
        }
    }

    /// <summary>Applies a response the caller already has, avoiding a second round trip.</summary>
    public void Apply(TenantPresentationResponse response)
    {
        _overrides = response.Overrides ?? new TenantTerminology();
        _locale = response.Locale;
        _tenantName = response.TenantName;

        _presentation = TenantPresentation.From(new TenantPresentationSettings
        {
            Terminology = _overrides,
            Locale = response.Locale is null
                ? null
                : new TenantLocale
                {
                    CurrencyCode = response.Locale.CurrencyCode,
                    CurrencySymbol = response.Locale.CurrencySymbol,
                    Culture = response.Locale.Culture,
                    TimeZoneId = response.Locale.TimeZoneId
                },
            Branding = response.Branding
        });

        _loaded = true;
        Changed?.Invoke();
    }

    // ---- Terminology shorthands, so markup reads naturally ----

    public string Term(TerminologyTerm term) => Terminology.Singular(term);

    public string Terms(TerminologyTerm term) => Terminology.Plural(term);

    /// <summary>Singular or plural by count: "1 Enquiry", "4 Enquiries".</summary>
    public string TermFor(TerminologyTerm term, int count) => Terminology.ForCount(term, count);

    /// <summary>"4 Enquiries" — count and term together, the common case in headings.</summary>
    public string Count(TerminologyTerm term, int count) => $"{count:#,##0} {TermFor(term, count)}";

    // ---- Formatting shorthands ----

    public string Money(decimal value) => Formatting.Amount(value);

    public string Money(decimal? value) => Formatting.Nullable(value);

    public string MoneyCompact(decimal value) => Formatting.Compact(value);

    public string Date(DateTime utc) => Formatting.Date(utc);

    public string DateTimeShort(DateTime utc) => Formatting.DateTimeShort(utc);

    public string DateTimeWithZone(DateTime utc) => Formatting.DateTimeWithZone(utc);

    /// <summary>The tenant's today, for date-range presets that must mean the tenant's day.</summary>
    public DateOnly Today() => Formatting.Today();
}
