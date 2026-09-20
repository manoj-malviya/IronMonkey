using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Features.Communications.Providers;
using IronMonkey.Data;
using IronMonkey.Data.Communications;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Workflow;

namespace IronMonkey.ApiService.Features.Communications;

/// <summary>What the caller wants sent, before a provider or suppression is considered.</summary>
public sealed record SendMessageCommand(
    Guid TenantId,
    MessageChannel Channel,
    string To,
    string? Subject,
    string Body,
    /// <summary>
    /// Stable across retries of the same logical send. Supplied by the caller rather than
    /// generated here: generated per call, it would change on each retry and defeat itself.
    /// </summary>
    string IdempotencyKey,
    Guid? LeadId = null,
    Guid? ContactId = null,
    Guid? SentByUserId = null,
    Guid? WorkflowRuleId = null);

/// <summary>Why a message was refused before it reached a provider.</summary>
public sealed record DispatchOutcome(
    Guid? MessageId,
    MessageStatus Status,
    MessageErrorCategory ErrorCategory,
    string? ErrorMessage)
{
    public bool Accepted => Status is MessageStatus.Sent or MessageStatus.Delivered;
    public bool Queued => Status == MessageStatus.Queued;
}

public interface IMessageDispatcher
{
    /// <summary>
    /// Persists the message and enqueues it. Does not contact a provider — the HTTP request
    /// that triggers a send must never block on one.
    /// </summary>
    Task<DispatchOutcome> QueueAsync(TenantDbContext db, SendMessageCommand command, CancellationToken cancellationToken);

    /// <summary>
    /// Performs the actual provider call for an already-persisted message. Called from the
    /// Hangfire job, and directly by tests.
    /// </summary>
    Task<DispatchOutcome> DeliverAsync(TenantDbContext db, Guid messageId, CancellationToken cancellationToken);
}

/// <summary>
/// The one path every outbound message takes, whoever asked for it.
///
/// <para>Suppression is enforced <b>here</b> rather than at each call site, and that is the
/// whole point of the type. An opt-out honoured in the "send from lead" endpoint but not in
/// the workflow engine is not an opt-out — and a call site added later would have to remember
/// to re-implement it. Routing every sender through one gate makes the guarantee structural
/// instead of a convention.</para>
///
/// <para>Queueing and delivery are split so a provider is never touched during an HTTP
/// request. The row is written first, in <see cref="MessageStatus.Queued"/>, so a crash
/// between persisting and sending leaves evidence rather than silence — the same reason the
/// workflow execution recorder writes its row before the action runs.</para>
/// </summary>
public sealed class MessageDispatcher(
    IMessageProviderRegistry providers,
    IMessageSendScheduler scheduler,
    ISuppressionService suppression,
    TimeProvider timeProvider,
    ILogger<MessageDispatcher> logger,
    IMessagingPolicyService? policyService = null) : IMessageDispatcher
{
    public async Task<DispatchOutcome> QueueAsync(
        TenantDbContext db, SendMessageCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // An existing row under this key means the caller (or a retry of it) already queued
        // this message. Returning the original rather than creating a second row is what makes
        // the whole send idempotent from end to end.
        var existing = await db.Messages
            .FirstOrDefaultAsync(m => m.IdempotencyKey == command.IdempotencyKey, cancellationToken);

        if (existing is not null)
        {
            logger.LogDebug(
                "Message with idempotency key {Key} already exists as {MessageId} in status {Status}; not re-queued",
                command.IdempotencyKey, existing.Id, existing.Status);

            return new DispatchOutcome(existing.Id, existing.Status, existing.ErrorCategory, existing.ErrorMessage);
        }

        var provider = providers.For(command.Channel);

        // A message on an unconfigured channel is still recorded, as Rejected. Recording it
        // is deliberate: the user pressed send, and a message that simply vanishes with a
        // toast is worse than one whose timeline row says why it never went.
        if (provider is null)
        {
            var rejected = Persist(db, command, fromAddress: string.Empty, now);
            rejected.MarkRejected(MessageErrorCategory.ChannelNotConfigured,
                $"{command.Channel} is not configured for this deployment.", now);

            await db.SaveChangesAsync(cancellationToken);
            return Outcome(rejected);
        }

        if (string.IsNullOrWhiteSpace(command.To))
        {
            var rejected = Persist(db, command, provider.FromAddress, now);
            rejected.MarkRejected(MessageErrorCategory.InvalidRecipient, "No recipient address.", now);
            await db.SaveChangesAsync(cancellationToken);
            return Outcome(rejected);
        }

        // Checked at queue time so a suppressed recipient never reaches a provider, and again
        // at delivery time in case the opt-out arrives while the message sits in the queue.
        if (await suppression.IsSuppressedAsync(db, command.Channel, command.To, cancellationToken))
        {
            var rejected = Persist(db, command, provider.FromAddress, now);
            rejected.MarkRejected(MessageErrorCategory.RecipientSuppressed,
                "The recipient has opted out of this channel.", now);

            await db.SaveChangesAsync(cancellationToken);
            return Outcome(rejected);
        }

        var ruleViolation = ChannelRules.Validate(command.Channel, command.Subject, command.Body);
        if (ruleViolation is not null)
        {
            var rejected = Persist(db, command, provider.FromAddress, now);
            rejected.MarkRejected(MessageErrorCategory.ChannelRuleViolation, ruleViolation, now);
            await db.SaveChangesAsync(cancellationToken);
            return Outcome(rejected);
        }

        // Quiet hours and the rate limit, evaluated in the tenant's own timezone. Optional so
        // the dispatcher can be constructed directly in a test; absent, there is no policy to
        // apply, which is the same as an unrestricted tenant.
        var decision = policyService is null
            ? null
            : await policyService.EvaluateAsync(db, command.TenantId, command.Channel, cancellationToken);

        var message = Persist(db, command, provider.FromAddress, now);

        if (decision is not null && !decision.IsDeferral)
        {
            // A hard refusal — the rate limit. Recorded rather than dropped, so the tenant can
            // see that the limit, not a provider, is what stopped the message.
            message.MarkRejected(decision.Category, decision.Reason, now);
            await db.SaveChangesAsync(cancellationToken);
            return Outcome(message);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (decision is { RetryAfterUtc: { } resumeAt })
        {
            // Deferred, not refused: the row stays Queued and the job is scheduled for when
            // the window closes. Rejecting it would lose a message the tenant asked to send.
            scheduler.Schedule(command.TenantId, message.Id, resumeAt);

            logger.LogInformation(
                "Message {MessageId} deferred until {ResumeAt:u} by the tenant's sending hours",
                message.Id, resumeAt);

            return Outcome(message);
        }

        // Enqueued only after the row is committed. Enqueueing first would race: a worker can
        // pick the job up before the transaction commits and find no row to send.
        scheduler.Enqueue(command.TenantId, message.Id);

        return Outcome(message);
    }

    public async Task<DispatchOutcome> DeliverAsync(
        TenantDbContext db, Guid messageId, CancellationToken cancellationToken)
    {
        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (message is null)
        {
            logger.LogWarning("Message {MessageId} not found for delivery", messageId);
            return new DispatchOutcome(null, MessageStatus.Rejected, MessageErrorCategory.UnexpectedError,
                "The message no longer exists.");
        }

        // The idempotency guard. A Hangfire retry of a job whose provider call already
        // succeeded stops here, so the customer does not get a second copy. This is why the
        // status is advanced to Sent before anything else can run.
        if (!message.CanAttemptSend())
        {
            logger.LogInformation(
                "Message {MessageId} is already {Status}; skipping send to avoid a duplicate",
                message.Id, message.Status);

            return Outcome(message);
        }

        var provider = providers.For(message.Channel);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (provider is null)
        {
            message.MarkRejected(MessageErrorCategory.ChannelNotConfigured,
                $"{message.Channel} is not configured for this deployment.", now);
            await db.SaveChangesAsync(cancellationToken);
            return Outcome(message);
        }

        // Re-checked at delivery: an opt-out that arrives between queueing and sending must
        // still be honoured, and a queued message can sit for minutes across a retry backoff.
        if (await suppression.IsSuppressedAsync(db, message.Channel, message.ToAddress, cancellationToken))
        {
            message.MarkRejected(MessageErrorCategory.RecipientSuppressed,
                "The recipient opted out before this message was sent.", now);
            await db.SaveChangesAsync(cancellationToken);
            return Outcome(message);
        }

        var request = new OutboundMessageRequest(
            message.Channel, provider.FromAddress, message.ToAddress,
            message.Subject, message.Body, message.IsBodyHtml, message.IdempotencyKey);

        SendResult result;
        try
        {
            result = await provider.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A provider throwing something unanticipated must not escape into the job and
            // roll back anything — it is recorded as a retryable failure like any other.
            logger.LogError(ex, "Provider {Provider} threw while sending message {MessageId}",
                provider.Name, message.Id);

            result = SendResult.Transient(MessageErrorCategory.UnexpectedError, ex.Message);
        }

        if (result.Accepted)
        {
            message.MarkSent(provider.Name, result.ProviderMessageId, now);
        }
        else
        {
            // Redacted before persisting: provider errors quote the request that caused them,
            // including credentials. Reusing the workflow redactor rather than writing a
            // second implementation keeps one set of rules to audit.
            var safeMessage = WorkflowDiagnosticRedactor.Redact(result.ErrorMessage);

            if (result.IsRetryable)
                message.MarkFailed(result.ErrorCategory, safeMessage, now);
            else
                message.MarkRejected(result.ErrorCategory, safeMessage, now);
        }

        await db.SaveChangesAsync(cancellationToken);

        // Rethrown so Hangfire schedules the retry. The row is already saved as Failed, so
        // the history survives regardless of what the retry does.
        if (!result.Accepted && result.IsRetryable)
            throw new MessageDeliveryException(message.Id, result.ErrorCategory, message.ErrorMessage);

        return Outcome(message);
    }

    private static Message Persist(TenantDbContext db, SendMessageCommand command, string fromAddress, DateTime now)
    {
        var message = Message.QueueOutbound(
            command.TenantId,
            command.Channel,
            fromAddress,
            command.To,
            command.Subject,
            command.Body,
            ChannelRules.IsHtmlBody(command.Channel),
            command.IdempotencyKey,
            now,
            command.LeadId,
            command.ContactId,
            command.SentByUserId,
            command.WorkflowRuleId);

        db.Messages.Add(message);
        return message;
    }

    private static DispatchOutcome Outcome(Message message) =>
        new(message.Id, message.Status, message.ErrorCategory, message.ErrorMessage);
}

/// <summary>
/// Signals a retryable delivery failure to Hangfire. Carries the message id so the retry is
/// traceable to the row rather than only to a job.
/// </summary>
public sealed class MessageDeliveryException(Guid messageId, MessageErrorCategory category, string? detail)
    : Exception($"Delivery of message {messageId} failed ({category}): {detail}")
{
    public Guid MessageId { get; } = messageId;
    public MessageErrorCategory Category { get; } = category;
}
