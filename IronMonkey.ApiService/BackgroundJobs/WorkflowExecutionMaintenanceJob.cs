using Hangfire;
using IronMonkey.ApiService.Features.Leads.Workflow.Execution;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IronMonkey.ApiService.BackgroundJobs;

/// <summary>
/// Keeps workflow execution history bounded and truthful, for every provisioned tenant.
///
/// Two jobs in one pass because both are sweeps over the same table on the same schedule:
///
///   1. Reconciliation — a row left <c>Running</c> past the staleness threshold is marked
///      <c>Abandoned</c>. Without this a worker that was killed mid-action leaves a row that
///      claims to still be running, forever, and the UI would show a run that never ends.
///      Abandoned rather than Failed because we genuinely do not know whether the action ran.
///
///   2. Retention — finished rows older than the retention window are deleted in batches.
///
/// Reconciliation runs first so a row that is both stale and past retention is deleted rather
/// than briefly resurrected as Abandoned.
/// </summary>
public class WorkflowExecutionMaintenanceJob(
    ITenantRegistry tenantRegistry,
    ITenantDbContextFactory tenantContextFactory,
    IOptions<WorkflowExecutionLogOptions> options,
    TimeProvider timeProvider,
    ILogger<WorkflowExecutionMaintenanceJob> logger)
{
    [AutomaticRetry(Attempts = 1)]
    [Queue("default")]
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var staleBefore = now.AddMinutes(-settings.EffectiveStaleThresholdMinutes);
        var retentionCutoff = now.AddDays(-settings.EffectiveRetentionDays);

        var tenants = await tenantRegistry.GetAllProvisionedTenantsAsync(cancellationToken);
        var totalAbandoned = 0;
        var totalDeleted = 0;

        foreach (var (tenantId, connectionString) in tenants)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var db = tenantContextFactory.CreateForTenant(connectionString, tenantId);

                var reconciled = await ReconcileStaleRunsAsync(db, tenantId, staleBefore, now, cancellationToken);
                totalAbandoned += reconciled.Count;

                if (settings.RetentionEnabled)
                {
                    totalDeleted += await ApplyRetentionAsync(
                        db, tenantId, retentionCutoff, settings.EffectiveBatchSize,
                        justReconciled: reconciled, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // One tenant's database being unreachable must not stop the others from being
                // maintained — the same isolation the time-elapsed scan uses.
                logger.LogError(ex, "Workflow execution maintenance failed for tenant {TenantId}", tenantId);
            }
        }

        logger.LogInformation(
            "Workflow execution maintenance complete: {Abandoned} stale run(s) reconciled, {Deleted} row(s) " +
            "removed past {RetentionDays}-day retention across {TenantCount} tenant(s)",
            totalAbandoned, totalDeleted, settings.EffectiveRetentionDays, tenants.Count);
    }

    /// <summary>
    /// Marks rows still <c>Running</c> since before the threshold as <c>Abandoned</c>.
    ///
    /// Loaded and mutated through the entity rather than with ExecuteUpdate so the transition
    /// goes through <c>MarkAbandoned</c> — which also stamps CompletedAt and DurationMs. A raw
    /// UPDATE of the status column alone would leave a row that is Abandoned but has no end
    /// time, and the UI renders duration from those columns.
    /// </summary>
    private async Task<IReadOnlyCollection<Guid>> ReconcileStaleRunsAsync(
        TenantDbContext db, Guid tenantId, DateTime staleBefore, DateTime now,
        CancellationToken cancellationToken)
    {
        var stale = await db.WorkflowExecutionLogs
            .Where(l => l.Status == WorkflowExecutionStatus.Running && l.StartedAt < staleBefore)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0) return [];

        foreach (var run in stale)
        {
            run.MarkAbandoned(
                "The run did not finish: the worker stopped before reporting an outcome. " +
                "Any actions it had already started may or may not have completed.",
                now);

            logger.LogWarning(
                "Reconciled stale workflow execution {ExecutionId} as Abandoned: tenant {TenantId}, " +
                "rule {RuleId}, lead {LeadId}, started {StartedAt:o}, correlation {CorrelationId}",
                run.Id, tenantId, run.WorkflowRuleId, run.LeadId, run.StartedAt, run.CorrelationId);
        }

        await db.SaveChangesAsync(cancellationToken);
        return stale.Select(l => l.Id).ToList();
    }

    /// <summary>
    /// Deletes finished rows older than the cutoff, in batches.
    ///
    /// <c>Running</c> rows are never deleted here: reconciliation above converts them first, so
    /// a run can only be removed once it has a truthful final status rather than disappearing
    /// while it still claims to be in flight.
    ///
    /// Rows this same pass just reconciled are also held back, for one cycle. Otherwise a run
    /// that was both stuck and past retention would be marked Abandoned and deleted in the same
    /// breath — nobody would ever see the Abandoned state it was given, and an operator
    /// investigating a stuck worker would find the evidence gone. They are removed on the next
    /// pass like anything else.
    ///
    /// Steps go with their parent.
    /// </summary>
    private async Task<int> ApplyRetentionAsync(
        TenantDbContext db, Guid tenantId, DateTime cutoff, int batchSize,
        IReadOnlyCollection<Guid> justReconciled, CancellationToken cancellationToken)
    {
        var reserved = justReconciled as ICollection<Guid> ?? justReconciled.ToList();
        var deleted = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ExecuteDeleteAsync issues one DELETE rather than loading every row — but it also
            // bypasses the cascade EF would have applied in memory, so the steps are deleted
            // explicitly first. The TenantId predicate is the global query filter's, which
            // ExecuteDelete does honour; it is restated on the step query because that one is
            // keyed by the parent ids.
            var expiring = await db.WorkflowExecutionLogs
                .Where(l => l.StartedAt < cutoff
                            && l.Status != WorkflowExecutionStatus.Running
                            && !reserved.Contains(l.Id))
                .OrderBy(l => l.StartedAt)
                .Select(l => l.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (expiring.Count == 0) break;

            await db.WorkflowExecutionSteps
                .Where(s => expiring.Contains(s.WorkflowExecutionLogId))
                .ExecuteDeleteAsync(cancellationToken);

            var removed = await db.WorkflowExecutionLogs
                .Where(l => expiring.Contains(l.Id))
                .ExecuteDeleteAsync(cancellationToken);

            deleted += removed;

            // A batch smaller than the cap means the backlog is drained.
            if (expiring.Count < batchSize) break;
        }

        if (deleted > 0)
        {
            logger.LogInformation(
                "Removed {Count} workflow execution row(s) older than {Cutoff:o} for tenant {TenantId}",
                deleted, cutoff, tenantId);
        }

        return deleted;
    }
}
