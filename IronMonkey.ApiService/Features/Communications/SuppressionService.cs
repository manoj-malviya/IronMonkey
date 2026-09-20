using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;
using IronMonkey.Data.Communications;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Communications;

public interface ISuppressionService
{
    Task<bool> IsSuppressedAsync(TenantDbContext db, MessageChannel channel, string? address, CancellationToken cancellationToken);

    /// <summary>Records an opt-out. Idempotent — opting out twice is not an error.</summary>
    Task OptOutAsync(TenantDbContext db, Guid tenantId, MessageChannel channel, string address,
        string source, CancellationToken cancellationToken);

    Task OptInAsync(TenantDbContext db, Guid tenantId, MessageChannel channel, string address,
        string source, CancellationToken cancellationToken);
}

/// <summary>
/// Reads and writes channel consent.
///
/// Every address goes through <see cref="ConsentAddress.Normalize"/> on both the read and the
/// write path. That symmetry is the correctness property: a STOP reply arriving as
/// "+1 (555) 010-9999" must suppress a send addressed to "15550109999", or the opt-out
/// silently fails to apply and the customer keeps being messaged.
/// </summary>
public sealed class SuppressionService(ILogger<SuppressionService> logger, TimeProvider timeProvider)
    : ISuppressionService
{
    public async Task<bool> IsSuppressedAsync(
        TenantDbContext db, MessageChannel channel, string? address, CancellationToken cancellationToken)
    {
        var normalized = ConsentAddress.Normalize(channel, address);

        // An empty address cannot be matched against a stored opt-out. It is rejected
        // elsewhere as an invalid recipient; reporting it as "not suppressed" here keeps the
        // two failures distinct in the message row.
        if (normalized.Length == 0) return false;

        return await db.MessageConsents
            .AsNoTracking()
            .AnyAsync(c => c.Channel == channel && c.Address == normalized && c.IsOptedOut, cancellationToken);
    }

    public async Task OptOutAsync(TenantDbContext db, Guid tenantId, MessageChannel channel,
        string address, string source, CancellationToken cancellationToken)
    {
        var normalized = ConsentAddress.Normalize(channel, address);
        if (normalized.Length == 0) return;

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var existing = await db.MessageConsents
            .FirstOrDefaultAsync(c => c.Channel == channel && c.Address == normalized, cancellationToken);

        if (existing is null)
            db.MessageConsents.Add(MessageConsent.OptOut(tenantId, channel, normalized, source, now));
        else
            existing.OptOutAgain(source, now);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Recorded {Channel} opt-out for tenant {TenantId} from {Source}",
            channel, tenantId, source);
    }

    public async Task OptInAsync(TenantDbContext db, Guid tenantId, MessageChannel channel,
        string address, string source, CancellationToken cancellationToken)
    {
        var normalized = ConsentAddress.Normalize(channel, address);
        if (normalized.Length == 0) return;

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var existing = await db.MessageConsents
            .FirstOrDefaultAsync(c => c.Channel == channel && c.Address == normalized, cancellationToken);

        // No row means no suppression, which is already "opted in" — so an opt-in for an
        // address that was never suppressed writes nothing rather than creating a row whose
        // only purpose is to say "normal".
        if (existing is null) return;

        existing.OptIn(source, now);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Recorded {Channel} opt-in for tenant {TenantId} from {Source}",
            channel, tenantId, source);
    }
}
