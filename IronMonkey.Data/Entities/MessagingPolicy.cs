using IronMonkey.Data.Abstractions;
using IronMonkey.Data.Communications;

namespace IronMonkey.Data.Entities;

/// <summary>
/// One tenant's outbound messaging policy: when it will not send, and how fast.
///
/// A single row per tenant. It lives in the tenant database rather than on the central tenant
/// row because — unlike terminology or branding, which the web shell needs before any
/// tenant-scoped query — this is only ever read on the send path, which already has a tenant
/// context open.
///
/// Its absence means "no restrictions", which is what makes this additive: a tenant
/// provisioned before this existed keeps sending exactly as it did.
/// </summary>
public sealed class MessagingPolicy : BaseTenantEntity
{
    private MessagingPolicy() { }

    /// <summary>Local start of the quiet window, e.g. 21:00. Null disables quiet hours.</summary>
    public TimeOnly? QuietHoursStart { get; private set; }

    /// <summary>Local end of the quiet window, e.g. 08:00, exclusive.</summary>
    public TimeOnly? QuietHoursEnd { get; private set; }

    /// <summary>
    /// Channels the window applies to, comma-separated. Empty means every channel.
    ///
    /// Stored flattened rather than as a jsonb collection because it is a short fixed-domain
    /// list read on the send path; a converter plus comparer would be more machinery than the
    /// value justifies.
    /// </summary>
    public string QuietHoursChannels { get; private set; } = string.Empty;

    /// <summary>
    /// Ceiling on outbound messages per channel per hour, or null for no limit. Guards against
    /// a misconfigured workflow rule emptying a tenant's provider quota overnight.
    /// </summary>
    public int? MaxMessagesPerHour { get; private set; }

    public static MessagingPolicy CreateDefault(Guid tenantId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId
    };

    public void SetQuietHours(TimeOnly? start, TimeOnly? end, IEnumerable<MessageChannel>? channels)
    {
        // Both ends are required for a window to mean anything; one alone is treated as unset
        // rather than as an open-ended window that would silence the tenant indefinitely.
        if (start is null || end is null)
        {
            QuietHoursStart = null;
            QuietHoursEnd = null;
            QuietHoursChannels = string.Empty;
            return;
        }

        QuietHoursStart = start;
        QuietHoursEnd = end;
        QuietHoursChannels = channels is null ? string.Empty : string.Join(',', channels);
    }

    public void SetRateLimit(int? maxPerHour) =>
        MaxMessagesPerHour = maxPerHour is > 0 ? maxPerHour : null;

    /// <summary>
    /// The window in the form the evaluator wants, or null when quiet hours are not configured.
    /// </summary>
    public QuietHoursWindow? ToWindow()
    {
        if (QuietHoursStart is null || QuietHoursEnd is null) return null;

        var channels = new List<MessageChannel>();

        foreach (var part in QuietHoursChannels.Split(',', StringSplitOptions.RemoveEmptyEntries
                                                          | StringSplitOptions.TrimEntries))
        {
            // An unparseable stored value is skipped rather than throwing: a renamed channel
            // must not make the whole policy unreadable and silently drop quiet hours.
            if (Enum.TryParse<MessageChannel>(part, ignoreCase: true, out var channel)
                && Enum.IsDefined(channel))
            {
                channels.Add(channel);
            }
        }

        return new QuietHoursWindow
        {
            Start = QuietHoursStart.Value,
            End = QuietHoursEnd.Value,
            Channels = channels
        };
    }
}
