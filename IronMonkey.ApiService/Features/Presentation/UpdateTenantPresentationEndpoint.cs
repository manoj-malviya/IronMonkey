using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Presentation;

namespace IronMonkey.ApiService.Features.Presentation;

/// <summary>
/// Updates the current tenant's terminology, locale and branding.
///
/// Gated on settings:write rather than bare authorization — this changes what every user in
/// the tenant sees, so it belongs with the other configuration surfaces rather than with
/// ordinary CRM work.
/// </summary>
public class UpdateTenantPresentationEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/tenant/presentation", Handle)
        .WithSummary("Update the current tenant's terminology, locale and branding")
        .WithTags("Presentation")
        .RequireAuthorization(PermissionConstants.SettingsWrite);

    private static async Task<Results<Ok<TenantPresentationResponse>, ValidationError, NotFound>> Handle(
        UpdateTenantPresentationRequest request,
        ITenantService tenantService,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        // Branding is rendered into the page, so it is validated before storage rather than
        // sanitized on every read. A rejected value is never persisted at all.
        if (BrandingValidation.Validate(request.Branding) is { } brandingError)
            return new ValidationError(brandingError);

        if (ValidateLocale(request.Locale) is { } localeError)
            return new ValidationError(localeError);

        if (ValidateTerminology(request.Terminology) is { } terminologyError)
            return new ValidationError(terminologyError);

        var tenantId = tenantService.GetCurrentTenantId();

        var tenant = await centralDb.Tenants
            .SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant is null)
            return TypedResults.NotFound();

        tenant.UpdatePresentation(new TenantPresentationSettings
        {
            Terminology = request.Terminology,
            Locale = request.Locale,
            Branding = request.Branding
        });

        await centralDb.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(PresentationMapping.ToResponse(tenant.Presentation, tenant.Name));
    }

    private const int MaxTermLength = 40;

    private static string? ValidateTerminology(TenantTerminology? terminology)
    {
        if (terminology is null)
            return null;

        foreach (var term in TerminologyDefaults.All)
        {
            var over = term switch
            {
                TerminologyTerm.Lead => terminology.Lead,
                TerminologyTerm.Contact => terminology.Contact,
                TerminologyTerm.Opportunity => terminology.Opportunity,
                TerminologyTerm.Task => terminology.Task,
                TerminologyTerm.Pipeline => terminology.Pipeline,
                TerminologyTerm.Stage => terminology.Stage,
                TerminologyTerm.Activity => terminology.Activity,
                _ => null
            };

            if (over is null)
                continue;

            if (Problem(over.Singular) is { } singularProblem)
                return $"{term} (singular): {singularProblem}";

            if (Problem(over.Plural) is { } pluralProblem)
                return $"{term} (plural): {pluralProblem}";
        }

        return null;

        // A blank term is legal and means "use the default" — it is not an error, so the
        // caller can clear an override by emptying the box.
        static string? Problem(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();

            if (trimmed.Length > MaxTermLength)
                return $"must be at most {MaxTermLength} characters.";

            // Terms are rendered as labels, page titles and document titles, so the same
            // markup characters refused for the branding display name are refused here.
            if (trimmed.Any(c => c is '<' or '>' or '"' or '\'' or '&') || trimmed.Any(char.IsControl))
                return "must not contain markup characters.";

            return null;
        }
    }

    private static string? ValidateLocale(TenantLocale? locale)
    {
        if (locale is null)
            return null;

        if (!string.IsNullOrWhiteSpace(locale.CurrencyCode) &&
            !System.Text.RegularExpressions.Regex.IsMatch(locale.CurrencyCode.Trim(), "^[A-Za-z]{3}$"))
            return "Currency code must be a three-letter ISO 4217 code, such as GBP.";

        if (!string.IsNullOrWhiteSpace(locale.CurrencySymbol) && locale.CurrencySymbol.Trim().Length > 5)
            return "Currency symbol must be at most 5 characters.";

        // The culture and zone are resolved with a fallback at render time, but an unknown
        // value is still rejected here: silently falling back to UTC on every page is worse
        // than telling the admin their entry was not recognised.
        if (!string.IsNullOrWhiteSpace(locale.Culture))
        {
            try
            {
                // predefinedOnly, because the plain lookup accepts any well-formed tag and
                // synthesizes a culture with no group separator — so "xx-YY" would validate
                // here and then silently render 1000 instead of 1,000 on every page.
                System.Globalization.CultureInfo.GetCultureInfo(locale.Culture.Trim(), predefinedOnly: true);
            }
            catch (System.Globalization.CultureNotFoundException)
            {
                return $"'{locale.Culture}' is not a recognised culture. Use a tag such as en-GB.";
            }
        }

        if (!string.IsNullOrWhiteSpace(locale.TimeZoneId))
        {
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(locale.TimeZoneId.Trim());
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return $"'{locale.TimeZoneId}' is not a recognised time zone. Use an IANA id such as Europe/London.";
            }
        }

        return null;
    }
}
