using IronMonkey.Data.Communications;
using IronMonkey.Data.Entities;

using Xunit;

namespace IronMonkey.Tests.Unit;

/// <summary>
/// The message status lifecycle.
///
/// The defect this whole feature replaces was inferring delivery from the absence of an error.
/// These tests pin the distinctions that prevent it: acceptance is not delivery, a retryable
/// failure is not a permanent one, and a message already handed to a provider can never be
/// sent again.
/// </summary>
public class MessageLifecycleTests
{
    private static readonly DateTime Now = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

    private static Message Outbound(MessageChannel channel = MessageChannel.Email) =>
        Message.QueueOutbound(Guid.NewGuid(), channel, "crm@example.com", "ada@example.com",
            "Subject", "Body", isBodyHtml: channel == MessageChannel.Email,
            $"key-{Guid.NewGuid():N}", Now, leadId: Guid.NewGuid());

    [Fact]
    public void A_new_outbound_message_is_queued_and_has_touched_no_provider()
    {
        var message = Outbound();

        Assert.Equal(MessageStatus.Queued, message.Status);
        Assert.Equal(MessageErrorCategory.None, message.ErrorCategory);
        Assert.Null(message.SentAt);
        Assert.Null(message.DeliveredAt);
        Assert.Empty(message.Provider);
        Assert.Null(message.ProviderMessageId);
    }

    [Fact]
    public void Acceptance_by_a_provider_is_Sent_not_Delivered()
    {
        // The critical distinction. A provider accepting a message says nothing about whether
        // the recipient ever got it, and conflating the two is exactly the old bug.
        var message = Outbound();

        message.MarkSent("Smtp", "provider-id", Now);

        Assert.Equal(MessageStatus.Sent, message.Status);
        Assert.NotEqual(MessageStatus.Delivered, message.Status);
        Assert.Equal(Now, message.SentAt);
        Assert.Null(message.DeliveredAt);
    }

    [Fact]
    public void Delivery_is_recorded_only_by_a_receipt()
    {
        var message = Outbound();
        message.MarkSent("Smtp", "provider-id", Now);

        var later = Now.AddMinutes(2);
        message.MarkDelivered(later);

        Assert.Equal(MessageStatus.Delivered, message.Status);
        Assert.Equal(later, message.DeliveredAt);
    }

    [Fact]
    public void A_queued_or_failed_message_may_be_attempted()
    {
        var queued = Outbound();
        Assert.True(queued.CanAttemptSend());

        var failed = Outbound();
        failed.MarkFailed(MessageErrorCategory.ProviderTimeout, "timed out", Now);
        Assert.True(failed.CanAttemptSend());
    }

    [Fact]
    public void A_sent_message_may_never_be_attempted_again()
    {
        // This is the guard that stops a Hangfire retry delivering a second copy to a
        // customer after a worker was killed between the provider accepting and the status
        // being written.
        var message = Outbound();
        message.MarkSent("Smtp", "provider-id", Now);

        Assert.False(message.CanAttemptSend());
    }

    [Fact]
    public void A_delivered_bounced_or_rejected_message_may_never_be_attempted_again()
    {
        var delivered = Outbound();
        delivered.MarkSent("Smtp", "id", Now);
        delivered.MarkDelivered(Now);
        Assert.False(delivered.CanAttemptSend());

        var bounced = Outbound();
        bounced.MarkSent("Smtp", "id", Now);
        bounced.MarkBounced("no such mailbox", Now);
        Assert.False(bounced.CanAttemptSend());

        var rejected = Outbound();
        rejected.MarkRejected(MessageErrorCategory.RecipientSuppressed, "opted out", Now);
        Assert.False(rejected.CanAttemptSend());
    }

    [Fact]
    public void A_successful_send_clears_a_previous_failures_error()
    {
        // Otherwise a message that failed once and then succeeded would display a stale error
        // alongside a Sent status.
        var message = Outbound();
        message.MarkFailed(MessageErrorCategory.ProviderTimeout, "timed out", Now);

        message.MarkSent("Smtp", "provider-id", Now.AddMinutes(1));

        Assert.Equal(MessageErrorCategory.None, message.ErrorCategory);
        Assert.Null(message.ErrorMessage);
    }

    [Fact]
    public void Rejection_and_failure_are_distinct_states()
    {
        var failed = Outbound();
        failed.MarkFailed(MessageErrorCategory.ProviderTimeout, "timed out", Now);

        var rejected = Outbound();
        rejected.MarkRejected(MessageErrorCategory.RecipientSuppressed, "opted out", Now);

        Assert.Equal(MessageStatus.Failed, failed.Status);
        Assert.Equal(MessageStatus.Rejected, rejected.Status);

        // Only the retryable one stays eligible. Retrying a suppressed recipient could never
        // help and would just burn attempts.
        Assert.True(failed.CanAttemptSend());
        Assert.False(rejected.CanAttemptSend());
    }

    [Fact]
    public void A_non_email_channel_carries_no_subject_even_if_one_is_supplied()
    {
        var sms = Message.QueueOutbound(Guid.NewGuid(), MessageChannel.Sms, "+1555", "+1556",
            "A subject", "Body", isBodyHtml: false, "key", Now);

        Assert.Null(sms.Subject);
    }

    [Fact]
    public void An_inbound_message_arrives_already_delivered()
    {
        // There is no handover to wait for — it is already here.
        var inbound = Message.RecordInbound(Guid.NewGuid(), MessageChannel.Sms, "Twilio", "SM123",
            "+15550109999", "+15550100000", null, "Hello", Now, "inbound:SM123", leadId: Guid.NewGuid());

        Assert.Equal(MessageDirection.Inbound, inbound.Direction);
        Assert.Equal(MessageStatus.Delivered, inbound.Status);
        Assert.Equal(Now, inbound.DeliveredAt);
    }

    [Fact]
    public void An_inbound_message_matched_to_no_record_is_flagged_unmatched()
    {
        var unmatched = Message.RecordInbound(Guid.NewGuid(), MessageChannel.Sms, "Twilio", "SM124",
            "+15550109999", "+15550100000", null, "Hello", Now, "inbound:SM124");

        Assert.True(unmatched.IsUnmatched);

        // Attaching it from the review queue clears the flag rather than creating a new row.
        unmatched.AttachTo(Guid.NewGuid(), null);
        Assert.False(unmatched.IsUnmatched);
    }

    [Fact]
    public void An_outbound_message_is_never_unmatched_regardless_of_links()
    {
        // IsUnmatched is a review-queue concept for inbound mail only. An outbound message
        // with no lead would otherwise appear in the queue asking to be matched to itself.
        var message = Message.QueueOutbound(Guid.NewGuid(), MessageChannel.Email, "a@b.com", "c@d.com",
            "s", "b", true, "key", Now);

        Assert.False(message.IsUnmatched);
    }

    [Fact]
    public void A_bounce_records_the_provider_rejection_category()
    {
        var message = Outbound();
        message.MarkSent("Smtp", "id", Now);
        message.MarkBounced("mailbox does not exist", Now.AddMinutes(1));

        Assert.Equal(MessageStatus.Bounced, message.Status);
        Assert.Equal(MessageErrorCategory.ProviderRejected, message.ErrorCategory);
        Assert.Equal("mailbox does not exist", message.ErrorMessage);
    }
}
