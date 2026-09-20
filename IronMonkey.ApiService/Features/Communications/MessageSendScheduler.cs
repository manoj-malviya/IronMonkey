using Hangfire;
using IronMonkey.ApiService.BackgroundJobs;

namespace IronMonkey.ApiService.Features.Communications;

/// <summary>
/// Enqueues the delivery job.
///
/// An interface rather than a direct <c>BackgroundJob.Enqueue</c> call so the dispatcher can
/// be constructed in a test without a Hangfire server, and so a test can assert that a
/// message was enqueued exactly once.
/// </summary>
public interface IMessageSendScheduler
{
    void Enqueue(Guid tenantId, Guid messageId);

    /// <summary>
    /// Enqueues the send for a future instant — used when quiet hours defer a message. A
    /// deferral must not drop the message: the tenant asked for it to go, just not now.
    /// </summary>
    void Schedule(Guid tenantId, Guid messageId, DateTime runAtUtc);
}

public sealed class HangfireMessageSendScheduler(IBackgroundJobClient jobClient) : IMessageSendScheduler
{
    public void Enqueue(Guid tenantId, Guid messageId) =>
        jobClient.Enqueue<MessageDeliveryJob>(job =>
            job.ExecuteAsync(tenantId, messageId, null, CancellationToken.None));

    public void Schedule(Guid tenantId, Guid messageId, DateTime runAtUtc) =>
        jobClient.Schedule<MessageDeliveryJob>(
            job => job.ExecuteAsync(tenantId, messageId, null, CancellationToken.None),
            new DateTimeOffset(DateTime.SpecifyKind(runAtUtc, DateTimeKind.Utc)));
}
