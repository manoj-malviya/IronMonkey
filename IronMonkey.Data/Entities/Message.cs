using IronMonkey.Data.Abstractions;
using IronMonkey.Data.Communications;

namespace IronMonkey.Data.Entities;

/// <summary>
/// One message on one channel, in one direction, attached to the CRM record it concerns.
///
/// This is conversation history, not a campaign log: every row links to a lead or a contact
/// (or, for an unmatched inbound message, to neither — see <see cref="IsUnmatched"/>), and
/// the lead/contact timeline is the intended way to read it.
///
/// The body is stored here rather than only at the provider because the timeline has to
/// render it after the provider's retention window closes. It is <em>not</em> copied into
/// workflow diagnostics — see the redaction rule in <c>WorkflowDiagnosticRedactor</c>.
/// </summary>
public sealed class Message : BaseTenantEntity
{
    private Message() { }

    public MessageChannel Channel { get; private set; }
    public MessageDirection Direction { get; private set; }

    /// <summary>
    /// Which provider handled it, e.g. "Smtp", "Twilio", "Noop". Recorded per message rather
    /// than read from configuration at display time, because configuration changes and the
    /// history must keep saying who actually carried this message.
    /// </summary>
    public string Provider { get; private set; } = string.Empty;

    /// <summary>
    /// The provider's own id for this message. Null until a provider accepts it. This is the
    /// key delivery receipts arrive against, so it is indexed.
    /// </summary>
    public string? ProviderMessageId { get; private set; }

    public string FromAddress { get; private set; } = string.Empty;
    public string ToAddress { get; private set; } = string.Empty;

    /// <summary>Email only. Null on SMS and WhatsApp, which have no subject.</summary>
    public string? Subject { get; private set; }

    public string Body { get; private set; } = string.Empty;

    /// <summary>
    /// True when <see cref="Body"/> is HTML rather than plain text. Only email sets this;
    /// it decides whether the timeline renders the body as markup or as text.
    /// </summary>
    public bool IsBodyHtml { get; private set; }

    public MessageStatus Status { get; private set; } = MessageStatus.Queued;
    public MessageErrorCategory ErrorCategory { get; private set; } = MessageErrorCategory.None;

    /// <summary>
    /// Redacted failure detail, safe to show a tenant Admin. Passed through
    /// <c>WorkflowDiagnosticRedactor.Redact</c> before it is ever set, because provider
    /// errors quote the request that caused them — including credentials.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    public DateTime QueuedAt { get; private set; }
    public DateTime? SentAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }
    public DateTime? FailedAt { get; private set; }

    /// <summary>The lead this message concerns, when it is about a lead.</summary>
    public Guid? LeadId { get; private set; }

    /// <summary>The contact this message concerns, when it is about a contact.</summary>
    public Guid? ContactId { get; private set; }

    /// <summary>The user who sent it. Null for workflow- and system-originated sends.</summary>
    public Guid? SentByUserId { get; private set; }

    /// <summary>The workflow rule that sent it, when a rule did. Null for manual sends.</summary>
    public Guid? WorkflowRuleId { get; private set; }

    /// <summary>
    /// The caller's idempotency token, unique per tenant.
    ///
    /// This is what stops a Hangfire retry delivering a second copy to a customer: the row is
    /// created once under this key, and every retry of the job resolves the same row and skips
    /// any message already past <see cref="MessageStatus.Queued"/>. A token generated at
    /// enqueue time (not at send time) is what makes that work across process restarts.
    /// </summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>
    /// Inbound only: true when the sender could not be matched to a lead or contact. These
    /// rows are deliberately kept and surfaced for review rather than dropped — an unmatched
    /// reply is usually a real customer whose number is recorded differently.
    /// </summary>
    public bool IsUnmatched => Direction == MessageDirection.Inbound && LeadId is null && ContactId is null;

    /// <summary>
    /// Opens an outbound message in <see cref="MessageStatus.Queued"/>. Nothing has been sent
    /// at this point, and the status deliberately says so — the row exists first so that a
    /// crash between persisting and sending leaves evidence rather than silence.
    /// </summary>
    public static Message QueueOutbound(
        Guid tenantId,
        MessageChannel channel,
        string fromAddress,
        string toAddress,
        string? subject,
        string body,
        bool isBodyHtml,
        string idempotencyKey,
        DateTime queuedAt,
        Guid? leadId = null,
        Guid? contactId = null,
        Guid? sentByUserId = null,
        Guid? workflowRuleId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Channel = channel,
            Direction = MessageDirection.Outbound,
            Provider = string.Empty,
            FromAddress = fromAddress,
            ToAddress = toAddress,
            Subject = channel == MessageChannel.Email ? subject : null,
            Body = body,
            IsBodyHtml = isBodyHtml,
            Status = MessageStatus.Queued,
            QueuedAt = queuedAt,
            IdempotencyKey = idempotencyKey,
            LeadId = leadId,
            ContactId = contactId,
            SentByUserId = sentByUserId,
            WorkflowRuleId = workflowRuleId
        };

    /// <summary>
    /// Records an inbound message. <paramref name="leadId"/> and <paramref name="contactId"/>
    /// may both be null — that is an unmatched message, which is kept for review.
    /// </summary>
    public static Message RecordInbound(
        Guid tenantId,
        MessageChannel channel,
        string provider,
        string? providerMessageId,
        string fromAddress,
        string toAddress,
        string? subject,
        string body,
        DateTime receivedAt,
        string idempotencyKey,
        Guid? leadId = null,
        Guid? contactId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Channel = channel,
            Direction = MessageDirection.Inbound,
            Provider = provider,
            ProviderMessageId = providerMessageId,
            FromAddress = fromAddress,
            ToAddress = toAddress,
            Subject = channel == MessageChannel.Email ? subject : null,
            Body = body,
            IsBodyHtml = false,
            // An inbound message has already arrived; there is no handover to wait for.
            Status = MessageStatus.Delivered,
            QueuedAt = receivedAt,
            DeliveredAt = receivedAt,
            IdempotencyKey = idempotencyKey,
            LeadId = leadId,
            ContactId = contactId
        };

    /// <summary>
    /// A provider accepted the message. Note this sets <see cref="MessageStatus.Sent"/>, not
    /// Delivered — acceptance is not delivery, and conflating them is the exact defect this
    /// lifecycle exists to prevent.
    /// </summary>
    public void MarkSent(string provider, string? providerMessageId, DateTime sentAt)
    {
        Provider = provider;
        ProviderMessageId = providerMessageId;
        Status = MessageStatus.Sent;
        SentAt = sentAt;
        ErrorCategory = MessageErrorCategory.None;
        ErrorMessage = null;
    }

    /// <summary>
    /// A delivery receipt confirmed the message arrived. Only a verified provider callback
    /// should call this.
    /// </summary>
    public void MarkDelivered(DateTime deliveredAt)
    {
        Status = MessageStatus.Delivered;
        DeliveredAt = deliveredAt;
    }

    /// <summary>A delivery receipt reported the message bounced after acceptance.</summary>
    public void MarkBounced(string? reason, DateTime bouncedAt)
    {
        Status = MessageStatus.Bounced;
        FailedAt = bouncedAt;
        ErrorCategory = MessageErrorCategory.ProviderRejected;
        ErrorMessage = reason;
    }

    /// <summary>
    /// The send failed in a way that may succeed on retry. The row stays available for the
    /// Hangfire retry to pick up.
    /// </summary>
    public void MarkFailed(MessageErrorCategory category, string? redactedMessage, DateTime failedAt)
    {
        Status = MessageStatus.Failed;
        ErrorCategory = category;
        ErrorMessage = redactedMessage;
        FailedAt = failedAt;
    }

    /// <summary>
    /// The send was refused permanently. Separate from <see cref="MarkFailed"/> so the job
    /// can tell "try again" from "trying again cannot help", and stop burning retries on a
    /// suppressed recipient or an unconfigured channel.
    /// </summary>
    public void MarkRejected(MessageErrorCategory category, string? redactedMessage, DateTime rejectedAt)
    {
        Status = MessageStatus.Rejected;
        ErrorCategory = category;
        ErrorMessage = redactedMessage;
        FailedAt = rejectedAt;
    }

    /// <summary>Attaches an inbound message to a record after the fact, from the review queue.</summary>
    public void AttachTo(Guid? leadId, Guid? contactId)
    {
        LeadId = leadId;
        ContactId = contactId;
    }

    /// <summary>
    /// Whether a send may still be attempted for this row.
    ///
    /// This is the idempotency guard: a Hangfire retry re-reads the row and stops here if a
    /// previous attempt already handed the message to a provider. Without it, the retry that
    /// exists to recover from a crashed worker would send the customer a duplicate.
    /// </summary>
    public bool CanAttemptSend() => Status is MessageStatus.Queued or MessageStatus.Failed;
}
