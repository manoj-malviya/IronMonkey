using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;
using IronMonkey.Data.Communications;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Communications.Webhooks;

/// <summary>An inbound message as the webhook received it, before it is matched to a record.</summary>
public sealed record InboundMessage(
    MessageChannel Channel,
    string Provider,
    string? ProviderMessageId,
    string From,
    string To,
    string? Subject,
    string Body);

public interface IInboundMessageService
{
    Task<Guid?> RecordAsync(TenantDbContext db, Guid tenantId, InboundMessage inbound, CancellationToken cancellationToken);

    Task ApplyDeliveryReceiptAsync(TenantDbContext db, string providerMessageId, MessageStatus status,
        string? reason, CancellationToken cancellationToken);
}

/// <summary>
/// Records inbound messages and delivery receipts against the right record.
///
/// Matching is best-effort and never destructive: a message that cannot be matched is still
/// stored, with no lead and no contact, and surfaces in the unmatched queue. Dropping it would
/// lose a real customer's reply because their number happened to be stored with different
/// punctuation.
/// </summary>
public sealed class InboundMessageService(
    ISuppressionService suppression,
    TimeProvider timeProvider,
    ILogger<InboundMessageService> logger) : IInboundMessageService
{
    /// <summary>
    /// Keywords that opt a sender out. Recognised on SMS and WhatsApp, where carriers and
    /// regulators require a keyword to work whether or not the business implements one.
    /// </summary>
    private static readonly string[] StopKeywords = ["stop", "stopall", "unsubscribe", "cancel", "end", "quit"];

    private static readonly string[] StartKeywords = ["start", "unstop", "yes"];

    public async Task<Guid?> RecordAsync(
        TenantDbContext db, Guid tenantId, InboundMessage inbound, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // The provider's id is the replay key. A provider retrying a callback it thinks we
        // missed must not add the customer's message to the timeline twice.
        var idempotencyKey = string.IsNullOrWhiteSpace(inbound.ProviderMessageId)
            ? $"inbound:{inbound.Channel}:{inbound.From}:{now.Ticks}"
            : $"inbound:{inbound.ProviderMessageId}";

        var existing = await db.Messages
            .FirstOrDefaultAsync(m => m.IdempotencyKey == idempotencyKey, cancellationToken);

        if (existing is not null)
        {
            logger.LogDebug("Inbound message {Key} already recorded as {MessageId}", idempotencyKey, existing.Id);
            return existing.Id;
        }

        var (leadId, contactId) = await MatchAsync(db, inbound, cancellationToken);

        var message = Message.RecordInbound(
            tenantId, inbound.Channel, inbound.Provider, inbound.ProviderMessageId,
            inbound.From, inbound.To, inbound.Subject, inbound.Body, now, idempotencyKey,
            leadId, contactId);

        db.Messages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        if (leadId is null && contactId is null)
        {
            logger.LogInformation(
                "Inbound {Channel} message {MessageId} for tenant {TenantId} matched no record and is queued for review",
                inbound.Channel, message.Id, tenantId);
        }

        await ApplyKeywordAsync(db, tenantId, inbound, cancellationToken);

        return message.Id;
    }

    public async Task ApplyDeliveryReceiptAsync(
        TenantDbContext db, string providerMessageId, MessageStatus status, string? reason,
        CancellationToken cancellationToken)
    {
        var message = await db.Messages
            .FirstOrDefaultAsync(m => m.ProviderMessageId == providerMessageId, cancellationToken);

        if (message is null)
        {
            // A receipt for an unknown id is normal, not an error: it can belong to another
            // environment sharing the provider account.
            logger.LogDebug("Delivery receipt for unknown provider message id");
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        switch (status)
        {
            case MessageStatus.Delivered:
                message.MarkDelivered(now);
                break;

            case MessageStatus.Bounced:
                message.MarkBounced(Data.Workflow.WorkflowDiagnosticRedactor.Redact(reason), now);

                // A hard bounce is a standing signal that this address does not work.
                // Suppressing it stops the tenant burning reputation on an address that will
                // keep bouncing.
                await suppression.OptOutAsync(db, message.TenantId, message.Channel,
                    message.ToAddress, "Bounce", cancellationToken);
                break;

            case MessageStatus.Failed:
                message.MarkFailed(MessageErrorCategory.ProviderRejected,
                    Data.Workflow.WorkflowDiagnosticRedactor.Redact(reason), now);
                break;

            default:
                // Anything else is a status we do not model — ignored rather than guessed at,
                // because writing a wrong status is worse than writing none.
                return;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Finds the lead or contact an inbound message is from.
    ///
    /// Leads are preferred over contacts because an active conversation is far more likely to
    /// concern an open lead than a converted contact. Both are matched on the normalized
    /// address, which is what makes "+1 (555) 010-9999" match a stored "15550109999".
    /// </summary>
    private static async Task<(Guid? LeadId, Guid? ContactId)> MatchAsync(
        TenantDbContext db, InboundMessage inbound, CancellationToken cancellationToken)
    {
        var normalized = ConsentAddress.Normalize(inbound.Channel, inbound.From);
        if (normalized.Length == 0) return (null, null);

        if (inbound.Channel == MessageChannel.Email)
        {
            // Email normalizes to a lowercase string, so this can be compared in the database
            // directly.
            var lead = await db.Leads
                .Where(l => l.Email.ToLower() == normalized)
                .Select(l => (Guid?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (lead is not null) return (lead, null);

            var contact = await db.Contacts
                .Where(c => c.Email.ToLower() == normalized)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return (null, contact);
        }

        // Phone numbers are stored with whatever punctuation the source used, so they cannot
        // be compared in SQL without a normalizing index. The candidate set is narrowed by the
        // last 7 digits — enough to make it selective — and the exact match is confirmed in
        // memory using the same normalizer the write path uses.
        var suffix = normalized.Length <= 7 ? normalized : normalized[^7..];

        var leadCandidates = await db.Leads
            .Where(l => l.Mobile != null && l.Mobile.Contains(suffix))
            .Select(l => new { l.Id, l.Mobile })
            .Take(50)
            .ToListAsync(cancellationToken);

        var matchedLead = leadCandidates
            .FirstOrDefault(c => ConsentAddress.Normalize(inbound.Channel, c.Mobile) == normalized);

        if (matchedLead is not null) return (matchedLead.Id, null);

        var contactCandidates = await db.Contacts
            .Where(c => c.Mobile != null && c.Mobile.Contains(suffix))
            .Select(c => new { c.Id, c.Mobile })
            .Take(50)
            .ToListAsync(cancellationToken);

        var matchedContact = contactCandidates
            .FirstOrDefault(c => ConsentAddress.Normalize(inbound.Channel, c.Mobile) == normalized);

        return (null, matchedContact?.Id);
    }

    /// <summary>
    /// Applies STOP/START keywords.
    ///
    /// Required on SMS and WhatsApp regardless of what the business wants: a customer replying
    /// STOP has withdrawn consent, and honouring it is not optional. Email opts out through an
    /// unsubscribe link instead, so keywords are not applied there — "STOP" in an email reply
    /// is ordinary prose.
    /// </summary>
    private async Task ApplyKeywordAsync(
        TenantDbContext db, Guid tenantId, InboundMessage inbound, CancellationToken cancellationToken)
    {
        if (inbound.Channel == MessageChannel.Email) return;

        var keyword = inbound.Body?.Trim().ToLowerInvariant() ?? string.Empty;

        if (StopKeywords.Contains(keyword))
        {
            await suppression.OptOutAsync(db, tenantId, inbound.Channel, inbound.From,
                "StopKeyword", cancellationToken);

            logger.LogInformation("Applied STOP keyword opt-out for tenant {TenantId} on {Channel}",
                tenantId, inbound.Channel);
        }
        else if (StartKeywords.Contains(keyword))
        {
            await suppression.OptInAsync(db, tenantId, inbound.Channel, inbound.From,
                "StartKeyword", cancellationToken);
        }
    }
}
