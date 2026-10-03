namespace IronMonkey.Data.Commerce;

/// <summary>
/// The single rounding rule for money in IronMonkey. Every call site — API, UI, quote
/// document, report — rounds through here, so none of them can disagree with another by a
/// penny.
///
/// <para><b>The rule.</b> Amounts are rounded to the currency's minor unit (2 places for most
/// currencies, 0 for JPY/KRW and similar, 3 for BHD/KWD/OMR/JOD/TND), with midpoints rounded
/// <b>away from zero</b> — 2.345 becomes 2.35, -2.345 becomes -2.35. That is what a customer
/// checking a quote with a calculator expects; .NET's default banker's rounding (2.345 →
/// 2.34) is correct statistics and a support ticket on an invoice.</para>
///
/// <para><b>Where it is applied.</b> Per line, never only on the grand total: see
/// <see cref="LineCalculator"/>. A document's total is the sum of the line figures the
/// customer can see, so the sum the customer does by hand always matches.</para>
/// </summary>
public static class MoneyMath
{
    private static readonly Dictionary<string, int> MinorUnitOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        // Zero-decimal currencies (ISO 4217 minor unit 0).
        ["BIF"] = 0, ["CLP"] = 0, ["DJF"] = 0, ["GNF"] = 0, ["ISK"] = 0, ["JPY"] = 0,
        ["KMF"] = 0, ["KRW"] = 0, ["PYG"] = 0, ["RWF"] = 0, ["UGX"] = 0, ["VND"] = 0,
        ["VUV"] = 0, ["XAF"] = 0, ["XOF"] = 0, ["XPF"] = 0,
        // Three-decimal currencies.
        ["BHD"] = 3, ["IQD"] = 3, ["JOD"] = 3, ["KWD"] = 3, ["LYD"] = 3, ["OMR"] = 3, ["TND"] = 3
    };

    /// <summary>Decimal places for a currency. Unknown or unset currencies use 2.</summary>
    public static int MinorUnits(string? currencyCode) =>
        currencyCode is not null && MinorUnitOverrides.TryGetValue(currencyCode, out var units) ? units : 2;

    /// <summary>Rounds to the currency's minor unit, midpoints away from zero.</summary>
    public static decimal Round(decimal amount, string? currencyCode) =>
        Math.Round(amount, MinorUnits(currencyCode), MidpointRounding.AwayFromZero);

    /// <summary>
    /// Normalises a currency code to upper-case ISO 4217 form, or null for blank. Returns
    /// false for anything that is not three ASCII letters — a currency the system cannot
    /// name is a currency it cannot refuse to mix.
    /// </summary>
    public static bool TryNormalizeCurrency(string? code, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(code)) return true;

        var trimmed = code.Trim().ToUpperInvariant();
        if (trimmed.Length != 3 || !trimmed.All(c => c is >= 'A' and <= 'Z')) return false;

        normalized = trimmed;
        return true;
    }

    /// <summary>
    /// Whether two currency codes denote the same currency. Null means "the tenant's own
    /// currency" (see <see cref="Entities.Opportunity.CurrencyCode"/>), so null matches the
    /// tenant base currency and another null.
    /// </summary>
    public static bool SameCurrency(string? a, string? b, string? baseCurrency)
    {
        var left = a ?? baseCurrency;
        var right = b ?? baseCurrency;
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
