using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;
using IronMonkey.Data.Communications;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Presentation;

namespace IronMonkey.ApiService.Features.Communications;

/// <summary>Why a policy refused or deferred a send, or null when it permits one.</summary>
public sealed record PolicyDecision(MessageErrorCategory Category, string Reason, DateTime? RetryAfterUtc)
{
    /// <summary>A deferral is not a refusal: the tenant wanted this sent, just not yet.</summary>
    public bool IsDeferral => RetryAfterUtc is not null;
}

public interface IMessagingPolicyService
{
    /// <summary>
    /// Evaluates quiet hours and the rate limit for one send. Returns null when it may proceed.
    /// </summary>
    Task<PolicyDecision?> EvaluateAsync(TenantDbContext db, Guid tenantId, MessageChannel channel,
        CancellationToken cancellationToken);
}

/// <summary>
/// Applies a tenant's outbound messaging policy.
///
/// Quiet hours are evaluated in the <em>tenant's</em> timezone, read from the central tenant
/// row's locale — the same setting the UI formats dates with. A server in UTC deciding "not
/// before 9am" for a tenant in Asia/Kolkata would message people in the middle of the night,
/// which is the entire failure quiet hours exist to prevent.
///
/// A tenant with no policy row is unrestricted, so this is additive: tenants provisioned
/// before it existed keep sending exactly as they did.
/// </summary>
public sealed class MessagingPolicyService(
    CentralDbContext centralDb,
    TimeProvider timeProvider,
    ILogger<MessagingPolicyService> logger) : IMessagingPolicyService
{
    public async Task<PolicyDecision?> EvaluateAsync(
        TenantDbContext db, Guid tenantId, MessageChannel channel, CancellationToken cancellationToken)
    {
        var policy = await db.MessagingPolicies.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        // No policy means no restrictions. Absence must read as "unrestricted" rather than as
        // an error, or adding this feature would silently block every existing tenant.
        if (policy is null) return null;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var zone = await ResolveTimeZoneAsync(tenantId, cancellationToken);

        var window = policy.ToWindow();

        if (QuietHours.IsQuiet(window, channel, now, zone))
        {
            var resume = QuietHours.NextAllowed(window, channel, now, zone);

            return new PolicyDecision(MessageErrorCategory.QuietHours,
                $"Outside sending hours for this organisation; scheduled for {resume:u}.", resume);
        }

        if (policy.MaxMessagesPerHour is { } limit)
        {
            var since = now.AddHours(-1);

            // Counts outbound messages in the last hour on this channel that actually consumed
            // capacity. Queued rows count — they are committed sends, and excluding them would
            // let a burst enqueue far past the limit before any reached a provider.
            //
            // Rejected rows are excluded, and that exclusion is load-bearing: a rejection is
            // recorded as a message row, so counting them would make each refusal push the
            // count higher and the window could never drain. The limit would latch on
            // permanently after the first breach.
            var recent = await db.Messages
                .CountAsync(m => m.Direction == MessageDirection.Outbound
                                 && m.Channel == channel
                                 && m.QueuedAt >= since
                                 && m.Status != MessageStatus.Rejected, cancellationToken);

            if (recent >= limit)
            {
                logger.LogWarning(
                    "Tenant {TenantId} hit its {Channel} rate limit of {Limit}/hour",
                    tenantId, channel, limit);

                return new PolicyDecision(MessageErrorCategory.RateLimited,
                    $"This organisation's limit of {limit} {channel} messages per hour has been reached.",
                    RetryAfterUtc: null);
            }
        }

        return null;
    }

    /// <summary>
    /// The tenant's configured zone, falling back to UTC.
    ///
    /// Reuses <see cref="TenantFormatting"/> so the zone quiet hours are judged in is exactly
    /// the one the tenant sees its timestamps rendered in — two resolutions would eventually
    /// disagree about what "9pm" means.
    /// </summary>
    private async Task<TimeZoneInfo> ResolveTimeZoneAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await centralDb.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        return TenantFormatting.From(tenant?.Presentation?.Locale).TimeZone;
    }
}
