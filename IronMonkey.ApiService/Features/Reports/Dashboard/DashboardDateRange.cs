namespace IronMonkey.ApiService.Features.Reports.Dashboard;

/// <summary>
/// Resolves the dashboard's date-range filter into a concrete half-open UTC interval.
///
/// The interval is deliberately half-open — <c>[From, ToExclusive)</c>. A closed upper
/// bound expressed as a date can only be written as "&lt;= end-of-day", and end-of-day has
/// no exact representation: <c>23:59:59.9999999</c> silently drops rows in the last tick,
/// and Postgres <c>timestamptz</c> keeps microsecond precision that a .NET tick-based
/// bound rounds against. Comparing <c>&lt; nextDay</c> has neither problem, so a record
/// created at any instant on the final day is always inside the range.
/// </summary>
public readonly record struct DashboardDateRange(DateTime From, DateTime ToExclusive, string Preset)
{
    /// <summary>Presets the UI offers; anything else falls back to <see cref="Last30Days"/>.</summary>
    public const string Today = "today";
    public const string Last7Days = "last7days";
    public const string Last30Days = "last30days";
    public const string ThisMonth = "thismonth";
    public const string ThisQuarter = "thisquarter";
    public const string ThisYear = "thisyear";
    public const string AllTime = "alltime";
    public const string Custom = "custom";

    /// <summary>
    /// Builds the range for a request. <paramref name="from"/>/<paramref name="to"/> are
    /// interpreted as whole days: <paramref name="to"/> is inclusive to the caller and is
    /// converted to the exclusive next-midnight bound described on this type.
    /// </summary>
    public static DashboardDateRange Resolve(string? preset, DateTime? from, DateTime? to, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var today = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);

        // A caller supplying explicit dates means "custom" even without saying so; honouring
        // the dates rather than the (possibly absent) preset keeps deep links working.
        var normalized = preset?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized) && (from.HasValue || to.HasValue))
            normalized = Custom;

        return normalized switch
        {
            Today => new(today, today.AddDays(1), Today),
            Last7Days => new(today.AddDays(-6), today.AddDays(1), Last7Days),
            ThisMonth => new(new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc), today.AddDays(1), ThisMonth),
            ThisQuarter => new(new DateTime(now.Year, ((now.Month - 1) / 3 * 3) + 1, 1, 0, 0, 0, DateTimeKind.Utc), today.AddDays(1), ThisQuarter),
            ThisYear => new(new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc), today.AddDays(1), ThisYear),

            // DateTime.MinValue is a legitimate lower bound here: every persisted CreatedAt
            // is greater, so the predicate stays in SQL and no special-casing is needed.
            AllTime => new(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc), today.AddDays(1), AllTime),

            Custom => ResolveCustom(from, to, today),
            _ => new(today.AddDays(-29), today.AddDays(1), Last30Days),
        };
    }

    private static DashboardDateRange ResolveCustom(DateTime? from, DateTime? to, DateTime today)
    {
        // Each side falls back independently, so "from only" and "to only" are both usable
        // rather than silently collapsing to the default preset.
        var start = from.HasValue
            ? DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc)
            : today.AddDays(-29);

        var endExclusive = to.HasValue
            ? DateTime.SpecifyKind(to.Value.Date, DateTimeKind.Utc).AddDays(1)
            : today.AddDays(1);

        // An inverted range would otherwise return zeros everywhere and read as "no data"
        // rather than "bad input"; swapping keeps the dashboard honest.
        if (endExclusive <= start)
            (start, endExclusive) = (endExclusive.AddDays(-1), start.AddDays(1));

        return new(start, endExclusive, Custom);
    }

    /// <summary>Inclusive last day, for echoing the range back to the UI.</summary>
    public DateTime ToInclusive => ToExclusive.AddDays(-1);
}
