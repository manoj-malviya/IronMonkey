using System.Globalization;

namespace IronMonkey.Web.Components.Shared;

/// <summary>
/// Formats monetary amounts for display.
///
/// Standard "C" formatting is not usable here: the server runs under the invariant
/// culture, whose currency symbol is the generic placeholder U+00A4 — so "C0" renders
/// "¤308,000" rather than an amount anyone can read. Forcing a specific culture instead
/// would be worse than wrong: it would assert a currency the tenant may not use.
///
/// So the amount is formatted with explicit grouping and no symbol, and callers label the
/// column or caption instead.
///
/// Per-tenant currency now exists: prefer <c>TenantPresentationService.Money</c>, which adds
/// the tenant's configured symbol and falls back to exactly this behaviour when a tenant has
/// configured none. This type remains for contexts with no tenant in scope — platform-admin
/// screens and anything rendered before the tenant's settings have loaded.
/// </summary>
public static class MoneyFormat
{
    /// <summary>Whole units with thousands separators: <c>308,000</c>.</summary>
    public static string Amount(decimal value) =>
        value.ToString("#,##0", CultureInfo.InvariantCulture);

    /// <summary>
    /// Shortened for headline figures where width is tight: <c>308K</c>, <c>1.2M</c>.
    /// Falls back to the full number below 10,000, where abbreviating loses precision
    /// that still matters.
    /// </summary>
    public static string Compact(decimal value)
    {
        var abs = Math.Abs(value);

        if (abs >= 1_000_000_000m)
            return Trim(value / 1_000_000_000m) + "B";
        if (abs >= 1_000_000m)
            return Trim(value / 1_000_000m) + "M";
        if (abs >= 10_000m)
            return Trim(value / 1_000m) + "K";

        return Amount(value);
    }

    // One decimal place, but only when it carries information: 1.2M stays 1.2M while
    // 2.0M reads as 2M.
    private static string Trim(decimal value) =>
        value.ToString("0.#", CultureInfo.InvariantCulture);

    public static string Nullable(decimal? value) =>
        value is { } v ? Amount(v) : "—";
}
