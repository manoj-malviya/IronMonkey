using IronMonkey.Data.Communications;

using Xunit;

namespace IronMonkey.Tests.Unit;

/// <summary>
/// Quiet hours, evaluated in the tenant's zone.
///
/// The overnight case is the one that matters: a 21:00–08:00 window has Start &gt; End and a
/// naive start &lt;= t &lt; end comparison reports it as never active, letting every
/// night-time message straight through — which is the entire failure quiet hours exist to
/// prevent.
/// </summary>
public class QuietHoursTests
{
    private static readonly TimeZoneInfo Kolkata = FindZone("Asia/Kolkata", "India Standard Time");
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static TimeZoneInfo FindZone(string iana, string windows)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(iana); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById(windows); }
    }

    private static QuietHoursWindow Overnight(params MessageChannel[] channels) => new()
    {
        Start = new TimeOnly(21, 0),
        End = new TimeOnly(8, 0),
        Channels = [.. channels]
    };

    [Fact]
    public void No_window_means_never_quiet()
    {
        Assert.False(QuietHours.IsQuiet(null, MessageChannel.Sms, DateTime.UtcNow, Utc));
    }

    [Fact]
    public void An_overnight_window_is_active_late_at_night()
    {
        var window = Overnight();
        var lateEvening = new DateTime(2026, 3, 10, 22, 30, 0, DateTimeKind.Utc);

        Assert.True(QuietHours.IsQuiet(window, MessageChannel.Sms, lateEvening, Utc));
    }

    [Fact]
    public void An_overnight_window_is_active_in_the_early_morning()
    {
        var window = Overnight();
        var earlyMorning = new DateTime(2026, 3, 10, 3, 0, 0, DateTimeKind.Utc);

        Assert.True(QuietHours.IsQuiet(window, MessageChannel.Sms, earlyMorning, Utc));
    }

    [Fact]
    public void An_overnight_window_is_not_active_during_the_day()
    {
        var window = Overnight();
        var midday = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

        Assert.False(QuietHours.IsQuiet(window, MessageChannel.Sms, midday, Utc));
    }

    [Fact]
    public void A_same_day_window_is_active_only_inside_it()
    {
        var window = new QuietHoursWindow { Start = new TimeOnly(12, 0), End = new TimeOnly(14, 0) };

        Assert.True(QuietHours.IsQuiet(window, MessageChannel.Sms,
            new DateTime(2026, 3, 10, 13, 0, 0, DateTimeKind.Utc), Utc));

        Assert.False(QuietHours.IsQuiet(window, MessageChannel.Sms,
            new DateTime(2026, 3, 10, 15, 0, 0, DateTimeKind.Utc), Utc));
    }

    [Fact]
    public void The_window_is_evaluated_in_the_tenants_zone_not_the_servers()
    {
        // 18:00 UTC is 23:30 in Kolkata — inside the window for that tenant and outside it
        // for a UTC tenant. A server deciding this in its own zone would message people in
        // the middle of the night.
        var window = Overnight();
        var instant = new DateTime(2026, 3, 10, 18, 0, 0, DateTimeKind.Utc);

        Assert.True(QuietHours.IsQuiet(window, MessageChannel.Sms, instant, Kolkata));
        Assert.False(QuietHours.IsQuiet(window, MessageChannel.Sms, instant, Utc));
    }

    [Fact]
    public void A_window_limited_to_sms_does_not_silence_email()
    {
        // Email commonly stays exempt: it sits in an inbox until read, while an SMS lights up
        // a handset.
        var window = Overnight(MessageChannel.Sms);
        var lateEvening = new DateTime(2026, 3, 10, 22, 30, 0, DateTimeKind.Utc);

        Assert.True(QuietHours.IsQuiet(window, MessageChannel.Sms, lateEvening, Utc));
        Assert.False(QuietHours.IsQuiet(window, MessageChannel.Email, lateEvening, Utc));
    }

    [Fact]
    public void An_empty_channel_list_applies_to_every_channel()
    {
        var window = Overnight();
        var lateEvening = new DateTime(2026, 3, 10, 22, 30, 0, DateTimeKind.Utc);

        foreach (var channel in Enum.GetValues<MessageChannel>())
            Assert.True(QuietHours.IsQuiet(window, channel, lateEvening, Utc));
    }

    [Fact]
    public void A_zero_length_window_means_no_quiet_hours_not_quiet_all_day()
    {
        var window = new QuietHoursWindow { Start = new TimeOnly(9, 0), End = new TimeOnly(9, 0) };

        Assert.False(QuietHours.IsQuiet(window, MessageChannel.Sms,
            new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc), Utc));
    }

    [Fact]
    public void Outside_quiet_hours_the_next_allowed_time_is_now()
    {
        var window = Overnight();
        var midday = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(midday, QuietHours.NextAllowed(window, MessageChannel.Sms, midday, Utc));
    }

    [Fact]
    public void In_the_late_evening_the_next_allowed_time_is_the_following_morning()
    {
        var window = Overnight();
        var lateEvening = new DateTime(2026, 3, 10, 22, 30, 0, DateTimeKind.Utc);

        var next = QuietHours.NextAllowed(window, MessageChannel.Sms, lateEvening, Utc);

        Assert.Equal(new DateTime(2026, 3, 11, 8, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void In_the_early_morning_the_next_allowed_time_is_the_same_day()
    {
        var window = Overnight();
        var earlyMorning = new DateTime(2026, 3, 10, 3, 0, 0, DateTimeKind.Utc);

        var next = QuietHours.NextAllowed(window, MessageChannel.Sms, earlyMorning, Utc);

        Assert.Equal(new DateTime(2026, 3, 10, 8, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void The_next_allowed_time_is_always_in_the_future_when_quiet()
    {
        var window = Overnight();

        foreach (var hour in new[] { 21, 22, 23, 0, 3, 7 })
        {
            var day = hour >= 21 ? 10 : 11;
            var instant = new DateTime(2026, 3, day, hour, 0, 0, DateTimeKind.Utc);

            var next = QuietHours.NextAllowed(window, MessageChannel.Sms, instant, Utc);

            Assert.True(next > instant, $"NextAllowed at {instant:o} returned {next:o}");
        }
    }
}
