namespace IronMonkey.Data.Communications;

/// <summary>
/// A window during which the tenant does not want outbound messages delivered.
///
/// Evaluated in the tenant's own timezone, not the server's. "No SMS before 9am" means 9am
/// where the customer is, and a server in UTC deciding that for a tenant in Asia/Kolkata
/// would message people in the middle of the night — the exact failure quiet hours exist to
/// prevent.
/// </summary>
public sealed class QuietHoursWindow
{
    /// <summary>Local start, inclusive. e.g. 21:00.</summary>
    public TimeOnly Start { get; set; }

    /// <summary>Local end, exclusive. e.g. 08:00.</summary>
    public TimeOnly End { get; set; }

    /// <summary>
    /// Channels the window applies to. Email is commonly exempt — an email sits in an inbox
    /// until it is read, while an SMS lights up a handset at 3am.
    /// </summary>
    public List<MessageChannel> Channels { get; set; } = [];
}

/// <summary>
/// Decides whether a send is inside a tenant's quiet hours, and when it may next go out.
/// </summary>
public static class QuietHours
{
    /// <summary>
    /// Whether <paramref name="utcNow"/> falls inside the window for this channel.
    ///
    /// Handles the overnight case, which is the normal one: a 21:00–08:00 window has
    /// Start &gt; End and covers two calendar days, so a naive <c>start &lt;= t &lt; end</c>
    /// comparison would report the window as never active and let every night-time message
    /// straight through.
    /// </summary>
    public static bool IsQuiet(QuietHoursWindow? window, MessageChannel channel, DateTime utcNow, TimeZoneInfo tenantZone)
    {
        if (window is null) return false;
        if (window.Channels.Count > 0 && !window.Channels.Contains(channel)) return false;

        // A zero-length window is "no quiet hours", not "quiet all day".
        if (window.Start == window.End) return false;

        var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), tenantZone));

        return window.Start < window.End
            // Same-day window: 12:00–14:00.
            ? local >= window.Start && local < window.End
            // Overnight window: 21:00–08:00 is "after 21:00 OR before 08:00".
            : local >= window.Start || local < window.End;
    }

    /// <summary>
    /// The next UTC instant at which a message may be delivered, given the window.
    ///
    /// Returns <paramref name="utcNow"/> unchanged when not in quiet hours. A deferred
    /// message is rescheduled rather than dropped: the tenant wanted it sent, just not now.
    /// </summary>
    public static DateTime NextAllowed(QuietHoursWindow? window, MessageChannel channel, DateTime utcNow, TimeZoneInfo tenantZone)
    {
        if (!IsQuiet(window, channel, utcNow, tenantZone)) return utcNow;

        var utc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, tenantZone);
        var localTime = TimeOnly.FromDateTime(local);

        // The end boundary is today when it is still ahead of us, tomorrow when we are in the
        // late-evening half of an overnight window and it has already passed for today.
        var endDate = local.Date;
        if (localTime >= window!.End)
            endDate = endDate.AddDays(1);

        var localResume = endDate.Add(window.End.ToTimeSpan());

        // A resume instant can land in a DST gap, where that wall-clock time does not exist.
        // Stepping forward an hour is correct and terminates: the gap is at most an hour.
        while (tenantZone.IsInvalidTime(localResume))
            localResume = localResume.AddHours(1);

        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localResume, DateTimeKind.Unspecified), tenantZone);
    }
}
