using Hangfire;
using IronMonkey.Data;
using Microsoft.EntityFrameworkCore;

namespace IronMonkey.ApiService.BackgroundJobs;

public class OutboxProcessingJob
{
    private readonly ITenantRegistry _tenantRegistry;
    private readonly ITenantDbContextFactory _tenantContextFactory;
    private readonly ILogger<OutboxProcessingJob> _logger;

    public OutboxProcessingJob(
        ITenantRegistry tenantRegistry,
        ITenantDbContextFactory tenantContextFactory,
        ILogger<OutboxProcessingJob> logger)
    {
        _tenantRegistry = tenantRegistry;
        _tenantContextFactory = tenantContextFactory;
        _logger = logger;
    }

    // TenantId MUST be the first parameter — explicit tenant context, no ambient state.
    // Hangfire serializes all parameters into the job payload — tenantId survives process restarts.
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 300])]
    [Queue("tenant")]
    public async Task ExecuteAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Processing outbox messages for tenant {TenantId}", tenantId);

        // Resolve connection string from central registry — NEVER from ambient context
        var connectionString = await _tenantRegistry.GetConnectionStringAsync(tenantId, cancellationToken);

        await using var db = _tenantContextFactory.CreateForTenant(connectionString, tenantId);

        var messages = await db.OutboxMessages
            .Where(m => !m.Published)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (!messages.Any())
        {
            _logger.LogDebug("No pending outbox messages for tenant {TenantId}", tenantId);
            return;
        }

        foreach (var message in messages)
        {
            try
            {
                // TODO Phase 2: Dispatch to domain event handlers
                // For Phase 1: mark as published (events are persisted, dispatch deferred)
                message.MarkAsPublished();
                _logger.LogDebug("Processed outbox message {MessageId} type {Type}", message.Id, message.Type);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process outbox message {MessageId} for tenant {TenantId}", message.Id, tenantId);
                // Don't rethrow per-message — continue processing others
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Processed {Count} outbox messages for tenant {TenantId}", messages.Count, tenantId);
    }
}
