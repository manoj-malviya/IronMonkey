using Hangfire;
using Hangfire.Server;
using IronMonkey.ApiService.Features.Communications;
using IronMonkey.Data;

namespace IronMonkey.ApiService.BackgroundJobs;

/// <summary>
/// Delivers one queued message.
///
/// Retries with backoff on the tenant queue. The retry is safe because
/// <see cref="MessageDispatcher.DeliverAsync"/> re-reads the row and refuses to send one that
/// is already past Queued — so a worker killed after the provider accepted the message, but
/// before the status was written, does not produce a second copy on the retry.
/// </summary>
public class MessageDeliveryJob(
    ITenantRegistry tenantRegistry,
    ITenantDbContextFactory tenantContextFactory,
    IMessageDispatcher dispatcher,
    ILogger<MessageDeliveryJob> logger)
{
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 300])]
    [Queue("tenant")]
    public async Task ExecuteAsync(Guid tenantId, Guid messageId,
        PerformContext? performContext, CancellationToken cancellationToken = default)
    {
        var jobId = performContext?.BackgroundJob.Id;

        logger.LogDebug("Delivering message {MessageId} for tenant {TenantId} (job {JobId})",
            messageId, tenantId, jobId);

        var connectionString = await tenantRegistry.GetConnectionStringAsync(tenantId, cancellationToken);
        await using var db = tenantContextFactory.CreateForTenant(connectionString, tenantId);

        // A retryable failure is rethrown by the dispatcher so Hangfire schedules the retry;
        // the row is already persisted as Failed by then, so the attempt is recorded whether
        // or not the retry eventually succeeds.
        var outcome = await dispatcher.DeliverAsync(db, messageId, cancellationToken);

        logger.LogInformation(
            "Message {MessageId} for tenant {TenantId} finished in status {Status} ({Category})",
            messageId, tenantId, outcome.Status, outcome.ErrorCategory);
    }
}
