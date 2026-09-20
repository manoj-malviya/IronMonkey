using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using IronMonkey.ApiService.Features.Communications;
using IronMonkey.ApiService.Features.Communications.Providers;
using IronMonkey.Data;
using IronMonkey.Data.Communications;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// The dispatcher against a real database.
///
/// These cover the feature's acceptance criteria that depend on persistence: idempotency
/// across retries, suppression enforced for every caller, honest status reporting, and tenant
/// isolation. No test here makes a network call or needs a provider credential — the fake
/// providers below stand in, and the no-op provider does so in the real application.
/// </summary>
[Collection("Integration")]
public class MessageDispatchTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;
    private readonly TenantDbContextFactory _factory = new();

    public MessageDispatchTests(PostgreSqlFixture fixture) => _fixture = fixture;

    /// <summary>Counts sends so a duplicate delivery is detectable rather than inferred.</summary>
    private sealed class CountingProvider(MessageChannel channel, SendResult result) : IMessageProvider
    {
        public int SendCount { get; private set; }
        public MessageChannel Channel { get; } = channel;
        public string Name => "Fake";
        public bool IsConfigured => true;
        public string FromAddress => Channel == MessageChannel.Email ? "crm@example.com" : "+15550100000";

        public Task<SendResult> SendAsync(OutboundMessageRequest request, CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingProvider(MessageChannel channel) : IMessageProvider
    {
        public MessageChannel Channel { get; } = channel;
        public string Name => "Throwing";
        public bool IsConfigured => true;
        public string FromAddress => "crm@example.com";

        public Task<SendResult> SendAsync(OutboundMessageRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("provider exploded");
    }

    private sealed class RecordingScheduler : IMessageSendScheduler
    {
        public List<Guid> Enqueued { get; } = [];

        /// <summary>Deferred sends, with the instant they were scheduled for.</summary>
        public List<(Guid MessageId, DateTime RunAtUtc)> Scheduled { get; } = [];

        public void Enqueue(Guid tenantId, Guid messageId) => Enqueued.Add(messageId);

        public void Schedule(Guid tenantId, Guid messageId, DateTime runAtUtc) =>
            Scheduled.Add((messageId, runAtUtc));
    }

    private static MessageDispatcher CreateDispatcher(
        IMessageSendScheduler scheduler, params IMessageProvider[] providers) =>
        new(new MessageProviderRegistry(providers),
            scheduler,
            new SuppressionService(NullLogger<SuppressionService>.Instance, TimeProvider.System),
            TimeProvider.System,
            NullLogger<MessageDispatcher>.Instance);

    private async Task<(TenantDbContext Db, Guid TenantId, string ConnectionString)> NewTenantAsync(string prefix)
    {
        var tenantId = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connectionString = _fixture.ConnectionString.Replace("ironmonkey_test", $"{prefix}_{suffix}");

        var db = _factory.CreateForTenant(connectionString, tenantId);
        await db.Database.EnsureCreatedAsync();

        return (db, tenantId, connectionString);
    }

    private static SendMessageCommand Command(Guid tenantId, string key,
        MessageChannel channel = MessageChannel.Email, string to = "ada@example.com") =>
        new(tenantId, channel, to, "Subject", "Body", key);

    [Fact]
    public async Task Queueing_persists_the_message_and_enqueues_it_exactly_once()
    {
        var (db, tenantId, _) = await NewTenantAsync("disp_queue");
        await using var _db = db;

        var scheduler = new RecordingScheduler();
        var dispatcher = CreateDispatcher(scheduler,
            new CountingProvider(MessageChannel.Email, SendResult.Success("pid")));

        var outcome = await dispatcher.QueueAsync(db, Command(tenantId, "k1"), default);

        Assert.True(outcome.Queued);
        Assert.Equal(MessageStatus.Queued, outcome.Status);
        Assert.Single(scheduler.Enqueued);

        var stored = await db.Messages.SingleAsync();
        Assert.Equal(MessageStatus.Queued, stored.Status);
        Assert.Equal(MessageDirection.Outbound, stored.Direction);
    }

    [Fact]
    public async Task Queueing_the_same_idempotency_key_twice_creates_one_message()
    {
        // A double-clicked Send button, or an HTTP retry, must not produce two messages.
        var (db, tenantId, _) = await NewTenantAsync("disp_idem_q");
        await using var _db = db;

        var scheduler = new RecordingScheduler();
        var dispatcher = CreateDispatcher(scheduler,
            new CountingProvider(MessageChannel.Email, SendResult.Success("pid")));

        var first = await dispatcher.QueueAsync(db, Command(tenantId, "same-key"), default);
        var second = await dispatcher.QueueAsync(db, Command(tenantId, "same-key"), default);

        Assert.Equal(first.MessageId, second.MessageId);
        Assert.Equal(1, await db.Messages.CountAsync());
        Assert.Single(scheduler.Enqueued);
    }

    [Fact]
    public async Task A_retry_after_a_successful_send_does_not_deliver_a_second_copy()
    {
        // The acceptance criterion for Hangfire retries. The first delivery succeeds; the
        // second call is the retry a killed worker would produce, and it must not reach the
        // provider again.
        var (db, tenantId, _) = await NewTenantAsync("disp_retry");
        await using var _db = db;

        var provider = new CountingProvider(MessageChannel.Email, SendResult.Success("pid"));
        var dispatcher = CreateDispatcher(new RecordingScheduler(), provider);

        var queued = await dispatcher.QueueAsync(db, Command(tenantId, "retry-key"), default);

        await dispatcher.DeliverAsync(db, queued.MessageId!.Value, default);
        await dispatcher.DeliverAsync(db, queued.MessageId!.Value, default);
        await dispatcher.DeliverAsync(db, queued.MessageId!.Value, default);

        Assert.Equal(1, provider.SendCount);

        var stored = await db.Messages.SingleAsync();
        Assert.Equal(MessageStatus.Sent, stored.Status);
    }

    [Fact]
    public async Task A_successful_send_is_recorded_as_Sent_not_Delivered()
    {
        var (db, tenantId, _) = await NewTenantAsync("disp_sent");
        await using var _db = db;

        var dispatcher = CreateDispatcher(new RecordingScheduler(),
            new CountingProvider(MessageChannel.Email, SendResult.Success("provider-id")));

        var queued = await dispatcher.QueueAsync(db, Command(tenantId, "sent-key"), default);
        await dispatcher.DeliverAsync(db, queued.MessageId!.Value, default);

        var stored = await db.Messages.SingleAsync();

        // Acceptance is not delivery. Only a verified receipt may write Delivered.
        Assert.Equal(MessageStatus.Sent, stored.Status);
        Assert.Equal("provider-id", stored.ProviderMessageId);
        Assert.Equal("Fake", stored.Provider);
        Assert.Null(stored.DeliveredAt);
    }

    [Fact]
    public async Task A_transient_provider_failure_is_recorded_as_Failed_and_rethrown_for_retry()
    {
        var (db, tenantId, _) = await NewTenantAsync("disp_transient");
        await using var _db = db;

        var dispatcher = CreateDispatcher(new RecordingScheduler(),
            new CountingProvider(MessageChannel.Email,
                SendResult.Transient(MessageErrorCategory.ProviderTimeout, "timed out")));

        var queued = await dispatcher.QueueAsync(db, Command(tenantId, "transient-key"), default);

        // Rethrown so Hangfire schedules the retry — but the row is saved first, so the
        // attempt is recorded whatever the retry does.
        await Assert.ThrowsAsync<MessageDeliveryException>(
            () => dispatcher.DeliverAsync(db, queued.MessageId!.Value, default));

        var stored = await db.Messages.SingleAsync();
        Assert.Equal(MessageStatus.Failed, stored.Status);
        Assert.Equal(MessageErrorCategory.ProviderTimeout, stored.ErrorCategory);
        Assert.True(stored.CanAttemptSend());
    }

    [Fact]
    public async Task A_permanent_provider_failure_is_Rejected_and_not_retried()
    {
        var (db, tenantId, _) = await NewTenantAsync("disp_permanent");
        await using var _db = db;

        var dispatcher = CreateDispatcher(new RecordingScheduler(),
            new CountingProvider(MessageChannel.Email,
                SendResult.Permanent(MessageErrorCategory.ProviderRejected, "bad credentials")));

        var queued = await dispatcher.QueueAsync(db, Command(tenantId, "permanent-key"), default);

        // No exception: retrying cannot help, so Hangfire is not asked to try again.
        await dispatcher.DeliverAsync(db, queued.MessageId!.Value, default);

        var stored = await db.Messages.SingleAsync();
        Assert.Equal(MessageStatus.Rejected, stored.Status);
        Assert.False(stored.CanAttemptSend());
    }

    [Fact]
    public async Task A_provider_that_throws_is_recorded_rather_than_escaping_unhandled()
    {
        var (db, tenantId, _) = await NewTenantAsync("disp_throw");
        await using var _db = db;

        var dispatcher = CreateDispatcher(new RecordingScheduler(), new ThrowingProvider(MessageChannel.Email));

        var queued = await dispatcher.QueueAsync(db, Command(tenantId, "throw-key"), default);

        await Assert.ThrowsAsync<MessageDeliveryException>(
            () => dispatcher.DeliverAsync(db, queued.MessageId!.Value, default));

        var stored = await db.Messages.SingleAsync();
        Assert.Equal(MessageStatus.Failed, stored.Status);
        Assert.Equal(MessageErrorCategory.UnexpectedError, stored.ErrorCategory);
    }

    [Fact]
    public async Task An_unconfigured_channel_is_rejected_and_still_recorded()
    {
        // The message is deliberately kept: the user pressed Send, and a message that
        // vanishes with a toast is worse than one whose row says why it never went.
        var (db, tenantId, _) = await NewTenantAsync("disp_unconfig");
        await using var _db = db;

        var scheduler = new RecordingScheduler();

        // Only email is registered, so SMS has no provider.
        var dispatcher = CreateDispatcher(scheduler,
            new CountingProvider(MessageChannel.Email, SendResult.Success("pid")));

        var outcome = await dispatcher.QueueAsync(db,
            Command(tenantId, "sms-key", MessageChannel.Sms, "+15550109999"), default);

        Assert.Equal(MessageStatus.Rejected, outcome.Status);
        Assert.Equal(MessageErrorCategory.ChannelNotConfigured, outcome.ErrorCategory);
        Assert.Empty(scheduler.Enqueued);
        Assert.Equal(1, await db.Messages.CountAsync());
    }

    [Fact]
    public async Task An_unconfigured_provider_is_treated_as_absent()
    {
        var (db, tenantId, _) = await NewTenantAsync("disp_notready");
        await using var _db = db;

        var dispatcher = CreateDispatcher(new RecordingScheduler(), new UnconfiguredProvider());

        var outcome = await dispatcher.QueueAsync(db, Command(tenantId, "notready-key"), default);

        Assert.Equal(MessageErrorCategory.ChannelNotConfigured, outcome.ErrorCategory);
    }

    private sealed class UnconfiguredProvider : IMessageProvider
    {
        public MessageChannel Channel => MessageChannel.Email;
        public string Name => "Unconfigured";
        public bool IsConfigured => false;
        public string FromAddress => string.Empty;

        public Task<SendResult> SendAsync(OutboundMessageRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("must never be called");
    }

    [Fact]
    public async Task An_opted_out_recipient_is_never_queued()
    {
        var (db, tenantId, _) = await NewTenantAsync("disp_optout");
        await using var _db = db;

        var provider = new CountingProvider(MessageChannel.Email, SendResult.Success("pid"));
        var scheduler = new RecordingScheduler();
        var dispatcher = CreateDispatcher(scheduler, provider);

        var suppression = new SuppressionService(NullLogger<SuppressionService>.Instance, TimeProvider.System);
        await suppression.OptOutAsync(db, tenantId, MessageChannel.Email, "ada@example.com", "Manual", default);

        var outcome = await dispatcher.QueueAsync(db, Command(tenantId, "optout-key"), default);

        Assert.Equal(MessageStatus.Rejected, outcome.Status);
        Assert.Equal(MessageErrorCategory.RecipientSuppressed, outcome.ErrorCategory);
        Assert.Empty(scheduler.Enqueued);
        Assert.Equal(0, provider.SendCount);
    }

    [Fact]
    public async Task Suppression_matches_regardless_of_how_the_address_is_punctuated()
    {
        // The opt-out is recorded from a STOP reply formatted one way and the send is
        // addressed another way. If normalization were not symmetric, the lookup would miss
        // and the customer would be messaged anyway.
        var (db, tenantId, _) = await NewTenantAsync("disp_norm");
        await using var _db = db;

        var dispatcher = CreateDispatcher(new RecordingScheduler(),
            new CountingProvider(MessageChannel.Sms, SendResult.Success("pid")));

        var suppression = new SuppressionService(NullLogger<SuppressionService>.Instance, TimeProvider.System);
        await suppression.OptOutAsync(db, tenantId, MessageChannel.Sms, "+1 (555) 010-9999", "StopKeyword", default);

        var outcome = await dispatcher.QueueAsync(db,
            Command(tenantId, "norm-key", MessageChannel.Sms, "15550109999"), default);

        Assert.Equal(MessageErrorCategory.RecipientSuppressed, outcome.ErrorCategory);
    }

    [Fact]
    public async Task An_opt_out_arriving_after_queueing_is_still_honoured_at_delivery()
    {
        // A queued message can sit through a retry backoff. An opt-out that lands in that
        // window must stop it, which is why suppression is re-checked at delivery.
        var (db, tenantId, _) = await NewTenantAsync("disp_late_optout");
        await using var _db = db;

        var provider = new CountingProvider(MessageChannel.Email, SendResult.Success("pid"));
        var dispatcher = CreateDispatcher(new RecordingScheduler(), provider);

        var queued = await dispatcher.QueueAsync(db, Command(tenantId, "late-key"), default);
        Assert.True(queued.Queued);

        var suppression = new SuppressionService(NullLogger<SuppressionService>.Instance, TimeProvider.System);
        await suppression.OptOutAsync(db, tenantId, MessageChannel.Email, "ada@example.com", "Manual", default);

        await dispatcher.DeliverAsync(db, queued.MessageId!.Value, default);

        Assert.Equal(0, provider.SendCount);

        var stored = await db.Messages.SingleAsync();
        Assert.Equal(MessageStatus.Rejected, stored.Status);
        Assert.Equal(MessageErrorCategory.RecipientSuppressed, stored.ErrorCategory);
    }

    [Fact]
    public async Task Opting_back_in_allows_sending_again()
    {
        var (db, tenantId, _) = await NewTenantAsync("disp_optin");
        await using var _db = db;

        var dispatcher = CreateDispatcher(new RecordingScheduler(),
            new CountingProvider(MessageChannel.Email, SendResult.Success("pid")));

        var suppression = new SuppressionService(NullLogger<SuppressionService>.Instance, TimeProvider.System);
        await suppression.OptOutAsync(db, tenantId, MessageChannel.Email, "ada@example.com", "Manual", default);
        await suppression.OptInAsync(db, tenantId, MessageChannel.Email, "ada@example.com", "Manual", default);

        var outcome = await dispatcher.QueueAsync(db, Command(tenantId, "optin-key"), default);

        Assert.True(outcome.Queued);

        // The row is kept rather than deleted, so the consent history survives.
        var consent = await db.MessageConsents.SingleAsync();
        Assert.False(consent.IsOptedOut);
    }

    [Fact]
    public async Task An_opt_out_on_one_channel_does_not_suppress_another()
    {
        var (db, tenantId, _) = await NewTenantAsync("disp_channel_scope");
        await using var _db = db;

        var dispatcher = CreateDispatcher(new RecordingScheduler(),
            new CountingProvider(MessageChannel.Email, SendResult.Success("pid")),
            new CountingProvider(MessageChannel.Sms, SendResult.Success("pid")));

        var suppression = new SuppressionService(NullLogger<SuppressionService>.Instance, TimeProvider.System);
        await suppression.OptOutAsync(db, tenantId, MessageChannel.Sms, "15550109999", "StopKeyword", default);

        var email = await dispatcher.QueueAsync(db, Command(tenantId, "email-ok"), default);
        var sms = await dispatcher.QueueAsync(db,
            Command(tenantId, "sms-blocked", MessageChannel.Sms, "15550109999"), default);

        Assert.True(email.Queued);
        Assert.Equal(MessageErrorCategory.RecipientSuppressed, sms.ErrorCategory);
    }

    [Fact]
    public async Task A_message_with_no_recipient_is_rejected_as_an_invalid_recipient()
    {
        var (db, tenantId, _) = await NewTenantAsync("disp_norecipient");
        await using var _db = db;

        var dispatcher = CreateDispatcher(new RecordingScheduler(),
            new CountingProvider(MessageChannel.Email, SendResult.Success("pid")));

        var outcome = await dispatcher.QueueAsync(db,
            new SendMessageCommand(tenantId, MessageChannel.Email, "", "Subject", "Body", "empty-key"), default);

        Assert.Equal(MessageStatus.Rejected, outcome.Status);
        Assert.Equal(MessageErrorCategory.InvalidRecipient, outcome.ErrorCategory);
    }

    [Fact]
    public async Task A_channel_rule_violation_is_rejected_before_any_provider_is_contacted()
    {
        var (db, tenantId, _) = await NewTenantAsync("disp_rule");
        await using var _db = db;

        var provider = new CountingProvider(MessageChannel.Email, SendResult.Success("pid"));
        var dispatcher = CreateDispatcher(new RecordingScheduler(), provider);

        // An email with no subject.
        var outcome = await dispatcher.QueueAsync(db,
            new SendMessageCommand(tenantId, MessageChannel.Email, "ada@example.com", null, "Body", "rule-key"),
            default);

        Assert.Equal(MessageStatus.Rejected, outcome.Status);
        Assert.Equal(MessageErrorCategory.ChannelRuleViolation, outcome.ErrorCategory);
        Assert.Equal(0, provider.SendCount);
    }

    [Fact]
    public async Task One_tenants_messages_and_opt_outs_are_invisible_to_another()
    {
        // Database-per-tenant plus the global query filter. Pinned here because a message
        // body is the most sensitive data in the CRM, and an opt-out leaking across tenants
        // would silently block a different tenant's legitimate sends.
        var (dbA, tenantA, _) = await NewTenantAsync("disp_iso_a");
        await using var _a = dbA;
        var (dbB, tenantB, _) = await NewTenantAsync("disp_iso_b");
        await using var _b = dbB;

        var dispatcher = CreateDispatcher(new RecordingScheduler(),
            new CountingProvider(MessageChannel.Email, SendResult.Success("pid")));

        var suppression = new SuppressionService(NullLogger<SuppressionService>.Instance, TimeProvider.System);
        await suppression.OptOutAsync(dbA, tenantA, MessageChannel.Email, "ada@example.com", "Manual", default);

        await dispatcher.QueueAsync(dbA, Command(tenantA, "a-key"), default);

        // Tenant B addresses the same person and is not affected by tenant A's opt-out.
        var outcomeB = await dispatcher.QueueAsync(dbB, Command(tenantB, "b-key"), default);

        Assert.True(outcomeB.Queued);
        Assert.Equal(1, await dbA.Messages.CountAsync());
        Assert.Equal(1, await dbB.Messages.CountAsync());
        Assert.Equal(0, await dbB.MessageConsents.CountAsync());
    }

    [Fact]
    public async Task Two_tenants_may_use_the_same_idempotency_key_without_colliding()
    {
        // The unique index is scoped by tenant, so one tenant cannot block another's send by
        // choosing a colliding key.
        var (dbA, tenantA, _) = await NewTenantAsync("disp_key_a");
        await using var _a = dbA;
        var (dbB, tenantB, _) = await NewTenantAsync("disp_key_b");
        await using var _b = dbB;

        var dispatcher = CreateDispatcher(new RecordingScheduler(),
            new CountingProvider(MessageChannel.Email, SendResult.Success("pid")));

        var a = await dispatcher.QueueAsync(dbA, Command(tenantA, "shared-key"), default);
        var b = await dispatcher.QueueAsync(dbB, Command(tenantB, "shared-key"), default);

        Assert.True(a.Queued);
        Assert.True(b.Queued);
        Assert.NotEqual(a.MessageId, b.MessageId);
    }

    [Fact]
    public async Task A_delivery_receipt_advances_a_sent_message_to_delivered()
    {
        var (db, tenantId, _) = await NewTenantAsync("disp_receipt");
        await using var _db = db;

        var dispatcher = CreateDispatcher(new RecordingScheduler(),
            new CountingProvider(MessageChannel.Email, SendResult.Success("provider-abc")));

        var queued = await dispatcher.QueueAsync(db, Command(tenantId, "receipt-key"), default);
        await dispatcher.DeliverAsync(db, queued.MessageId!.Value, default);

        var inbound = new IronMonkey.ApiService.Features.Communications.Webhooks.InboundMessageService(
            new SuppressionService(NullLogger<SuppressionService>.Instance, TimeProvider.System),
            TimeProvider.System,
            NullLogger<IronMonkey.ApiService.Features.Communications.Webhooks.InboundMessageService>.Instance);

        await inbound.ApplyDeliveryReceiptAsync(db, "provider-abc", MessageStatus.Delivered, null, default);

        var stored = await db.Messages.SingleAsync();
        Assert.Equal(MessageStatus.Delivered, stored.Status);
        Assert.NotNull(stored.DeliveredAt);
    }

    [Fact]
    public async Task A_bounce_receipt_suppresses_the_address()
    {
        // A hard bounce is a standing signal the address does not work. Suppressing stops the
        // tenant burning sender reputation on it.
        var (db, tenantId, _) = await NewTenantAsync("disp_bounce");
        await using var _db = db;

        var dispatcher = CreateDispatcher(new RecordingScheduler(),
            new CountingProvider(MessageChannel.Email, SendResult.Success("provider-bounce")));

        var queued = await dispatcher.QueueAsync(db, Command(tenantId, "bounce-key"), default);
        await dispatcher.DeliverAsync(db, queued.MessageId!.Value, default);

        var inbound = new IronMonkey.ApiService.Features.Communications.Webhooks.InboundMessageService(
            new SuppressionService(NullLogger<SuppressionService>.Instance, TimeProvider.System),
            TimeProvider.System,
            NullLogger<IronMonkey.ApiService.Features.Communications.Webhooks.InboundMessageService>.Instance);

        await inbound.ApplyDeliveryReceiptAsync(db, "provider-bounce", MessageStatus.Bounced,
            "mailbox does not exist", default);

        var stored = await db.Messages.SingleAsync();
        Assert.Equal(MessageStatus.Bounced, stored.Status);

        var consent = await db.MessageConsents.SingleAsync();
        Assert.True(consent.IsOptedOut);
        Assert.Equal("Bounce", consent.Source);
    }
}
