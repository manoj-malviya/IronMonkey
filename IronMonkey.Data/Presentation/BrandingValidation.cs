using System.Text.RegularExpressions;

namespace IronMonkey.Data.Presentation;

/// <summary>
/// Validates branding before it is stored.
///
/// These values are interpolated into the rendered page, so they are checked on the way in
/// rather than escaped on every use: a colour that is not a hex colour, or a logo URL that is
/// not an http(s)/relative reference, is rejected outright. That keeps <c>javascript:</c> and
/// <c>data:</c> URLs, CSS injection through a colour field, and markup in the display name
/// out of the stored value entirely.
/// </summary>
public static class BrandingValidation
{
    // Exactly #rgb or #rrggbb. Anything else — a CSS function, a named colour with a
    // trailing declaration, a url() — is refused rather than sanitized.
    private static readonly Regex HexColor = new(@"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.Compiled);

    public const int MaxDisplayNameLength = 100;
    public const int MaxLogoUrlLength = 2000;

    public static bool IsValidColor(string? value) =>
        string.IsNullOrWhiteSpace(value) || HexColor.IsMatch(value.Trim());

    public static bool IsValidDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var trimmed = value.Trim();

        if (trimmed.Length > MaxDisplayNameLength)
            return false;

        // Blazor escapes text it renders, but the display name also reaches places that are
        // not Razor text nodes (a document title, an email subject). Angle brackets and
        // quotes have no business in a company name, so refuse them at the door.
        return !trimmed.Any(c => c is '<' or '>' or '"' or '\'' or '&') &&
               !trimmed.Any(char.IsControl);
    }

    /// <summary>
    /// Accepts an absolute https URL or a site-relative path. http is allowed only for
    /// localhost, so a development logo works without permitting mixed content in production.
    /// </summary>
    public static bool IsValidLogoUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var trimmed = value.Trim();

        if (trimmed.Length > MaxLogoUrlLength)
            return false;

        // A protocol-relative URL ("//evil.example/x.png") would inherit the page scheme and
        // load a third-party asset; treat it as absolute and refuse it below rather than
        // letting it pass as a relative path.
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
            return false;

        if (trimmed.StartsWith('/'))
            return !trimmed.Contains("..", StringComparison.Ordinal);

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme == Uri.UriSchemeHttps)
            return true;

        return uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
    }

    /// <summary>Returns the first problem found, or null when the branding is storable.</summary>
    public static string? Validate(TenantBranding? branding)
    {
        if (branding is null)
            return null;

        if (!IsValidDisplayName(branding.DisplayName))
            return $"Display name must be at most {MaxDisplayNameLength} characters and contain no markup characters.";

        if (!IsValidLogoUrl(branding.LogoUrl))
            return "Logo URL must be a site-relative path or an absolute https URL.";

        if (!IsValidColor(branding.PrimaryColor))
            return "Primary colour must be a hex colour such as #1d4ed8.";

        if (!IsValidColor(branding.AccentColor))
            return "Accent colour must be a hex colour such as #1d4ed8.";

        return null;
    }
}
