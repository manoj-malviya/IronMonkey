using System.Globalization;

namespace IronMonkey.Data.Presentation;

/// <summary>
/// Formats money and dates for one tenant, with every fallback applied.
///
/// This is the type that finally answers the question MoneyFormat left open. Currency comes
/// from the tenant's own setting; an unconfigured tenant still gets a readable number rather
/// than the invariant culture's U+00A4 placeholder, which is what "C0" would produce.
///
/// Dates are stored UTC and rendered here in the tenant's zone. An unknown or unset zone
/// falls back to UTC and says so, rather than silently showing server-local time.
/// </summary>
public sealed class TenantFormatting
{
    private readonly CultureInfo _culture;
    private readonly string? _currencySymbol;

    private TenantFormatting(CultureInfo culture, string? currencySymbol, TimeZoneInfo timeZone, string timeZoneLabel)
    {
        _culture = culture;
        _currencySymbol = currencySymbol;
        TimeZone = timeZone;
        TimeZoneLabel = timeZoneLabel;
    }

    public TimeZoneInfo TimeZone { get; }

    /// <summary>Short label for ambiguous timestamps, e.g. "UTC" or "Europe/London".</summary>
    public string TimeZoneLabel { get; }

    public static TenantFormatting Default { get; } = From(null);

    public static TenantFormatting From(TenantLocale? locale)
    {
        var culture = ResolveCulture(locale?.Culture);
        var zone = ResolveTimeZone(locale?.TimeZoneId, out var label);

        var symbol = string.IsNullOrWhiteSpace(locale?.CurrencySymbol)
            ? null
            : locale!.CurrencySymbol!.Trim();

        return new TenantFormatting(culture, symbol, zone, label);
    }

    /// <summary>
    /// Whole units with the tenant's symbol when one is configured: <c>£308,000</c>.
    /// Without a configured symbol it degrades to the bare grouped number rather than
    /// asserting a currency the tenant may not use.
    /// </summary>
    public string Amount(decimal value)
    {
        var number = value.ToString("#,##0", _culture);
        return _currencySymbol is null ? number : _currencySymbol + number;
    }

    public string Nullable(decimal? value) => value is { } v ? Amount(v) : "—";

    /// <summary>
    /// Shortened for headline figures where width is tight: <c>£308K</c>, <c>£1.2M</c>.
    /// Falls back to the full number below 10,000, where abbreviating loses precision that
    /// still matters.
    /// </summary>
    public string Compact(decimal value)
    {
        var abs = Math.Abs(value);

        string number;
        if (abs >= 1_000_000_000m) number = Trim(value / 1_000_000_000m) + "B";
        else if (abs >= 1_000_000m) number = Trim(value / 1_000_000m) + "M";
        else if (abs >= 10_000m) number = Trim(value / 1_000m) + "K";
        else return Amount(value);

        return _currencySymbol is null ? number : _currencySymbol + number;
    }

    /// <summary>Converts a stored UTC instant into the tenant's wall-clock time.</summary>
    public DateTime ToTenantTime(DateTime utc)
    {
        // A value read back from Postgres timestamptz arrives as Utc; one constructed in
        // code may be Unspecified. Treating Unspecified as UTC matches how every timestamp
        // in this system is written (DateTime.UtcNow), and is safer than letting the
        // conversion throw on an otherwise valid row.
        var instant = utc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(utc, DateTimeKind.Utc)
            : utc.ToUniversalTime();

        return TimeZoneInfo.ConvertTimeFromUtc(instant, TimeZone);
    }

    /// <summary>The tenant's current local date — "today" as the tenant means it.</summary>
    public DateOnly Today() => DateOnly.FromDateTime(ToTenantTime(DateTime.UtcNow));

    public string Date(DateTime utc) => ToTenantTime(utc).ToString("d MMM yyyy", _culture);

    public string DateTimeShort(DateTime utc) => ToTenantTime(utc).ToString("d MMM yyyy HH:mm", _culture);

    /// <summary>Date and time with the zone named, for values where the zone is ambiguous.</summary>
    public string DateTimeWithZone(DateTime utc) => $"{DateTimeShort(utc)} ({TimeZoneLabel})";

    private string Trim(decimal value) => value.ToString("0.#", _culture);

    private static CultureInfo ResolveCulture(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return CultureInfo.InvariantCulture;

        try
        {
            // GetCultureInfo does NOT throw for an unknown-but-well-formed tag: ICU hands back
            // a synthesized culture whose number format has no group separator, so "1,000"
            // silently becomes "1000". Ask for the strict lookup instead, which does throw.
            return CultureInfo.GetCultureInfo(name.Trim(), predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string? id, out string label)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            label = "UTC";
            return TimeZoneInfo.Utc;
        }

        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
            label = id.Trim();
            return zone;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // An unusable stored zone must not take the page down. Fall back to UTC and
            // label it honestly, so a wrong configuration is visible rather than silent.
            label = "UTC";
            return TimeZoneInfo.Utc;
        }
    }
}
