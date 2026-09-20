using IronMonkey.Data;
using IronMonkey.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IronMonkey.ApiService.Features.Leads.Workflow.Execution;

public class WorkflowExecutionRecorder(
    ILogger<WorkflowExecutionRecorder> logger,
    TimeProvider timeProvider) : IWorkflowExecutionRecorder
{
    public async Task<WorkflowExecutionRun?> StartAsync(
        TenantDbContext db,
        WorkflowExecutionContext context,
        WorkflowRule rule,
        Lead lead,
        WorkflowTrigger trigger,
        CancellationToken cancellationToken)
    {
        try
        {
            // One correlation id covers every rule that the trigger evaluates, so the
            // per-rule identity is (correlation, attempt, rule). A Hangfire retry re-enters
            // with the same correlation and a higher attempt, which is what makes a retry
            // visible as a retry rather than a new run.
            var correlationId = context.CorrelationId;

            var existing = await db.WorkflowExecutionLogs
                .FirstOrDefaultAsync(
                    l => l.CorrelationId == correlationId
                         && l.Attempt == context.Attempt
                         && l.WorkflowRuleId == rule.Id,
                    cancellationToken);

            if (existing is not null)
            {
                // Hangfire can deliver the same attempt twice (a worker losing its lock and
                // the job being requeued). Re-recording would write a second "success" for
                // one piece of work, which is exactly the misleading duplicate the history is
                // supposed to prevent.
                if (existing.Status is not WorkflowExecutionStatus.Running)
                {
                    logger.LogInformation(
                        "Skipping duplicate workflow execution record for rule {RuleId}, lead {LeadId}: " +
                        "correlation {CorrelationId} attempt {Attempt} already finished as {Status}",
                        rule.Id, lead.Id, correlationId, context.Attempt, existing.Status);
                    return null;
                }

                // A row still Running for this attempt is the previous, interrupted delivery.
                // Resuming it keeps one row per attempt instead of orphaning a Running row the
                // reconciliation job would later mark Abandoned.
                logger.LogInformation(
                    "Resuming in-flight workflow execution {ExecutionId} for rule {RuleId}, correlation {CorrelationId}",
                    existing.Id, rule.Id, correlationId);

                return new WorkflowExecutionRun(db, existing, logger, timeProvider);
            }

            var log = WorkflowExecutionLog.Start(
                context.TenantId,
                rule,
                lead.Id,
                LeadDisplayName(lead),
                trigger,
                correlationId,
                context.JobId,
                context.Attempt,
                timeProvider.GetUtcNow().UtcDateTime);

            db.WorkflowExecutionLogs.Add(log);

            // Flushed before the rule runs: if the worker dies during an action, the Running
            // row is the evidence that the trigger was received, and reconciliation can mark
            // it Abandoned rather than the run vanishing.
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Workflow execution {ExecutionId} started: tenant {TenantId}, rule {RuleId} ({RuleName}), " +
                "lead {LeadId}, trigger {Trigger}, correlation {CorrelationId}, attempt {Attempt}",
                log.Id, context.TenantId, rule.Id, rule.Name, lead.Id, trigger, correlationId, context.Attempt);

            return new WorkflowExecutionRun(db, log, logger, timeProvider);
        }
        catch (DbUpdateException ex)
        {
            // The unique (tenant, correlation, attempt) index rejected a concurrent insert:
            // another worker is already recording this attempt. Proceeding unrecorded is the
            // correct outcome — the other worker owns the history for it.
            logger.LogWarning(ex,
                "Concurrent workflow execution record for rule {RuleId}, correlation {CorrelationId} attempt {Attempt}; " +
                "proceeding without a history row",
                rule.Id, context.CorrelationId, context.Attempt);

            // The failed insert is still tracked; detaching it keeps the engine's own
            // SaveChanges calls (assign, schedule_task) from retrying it and throwing again.
            DetachFailedInserts(db);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to open workflow execution history for rule {RuleId}, lead {LeadId}, tenant {TenantId}",
                rule.Id, lead.Id, context.TenantId);
            DetachFailedInserts(db);
            return null;
        }
    }

    /// <summary>
    /// Drops unsaved history entities from the change tracker.
    ///
    /// Without this a rejected insert stays Added, and the engine's next <c>SaveChangesAsync</c>
    /// — the one that assigns the lead or creates the task — would retry it and fail the
    /// business action because of an audit-row conflict.
    /// </summary>
    private static void DetachFailedInserts(TenantDbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added
                        && e.Entity is WorkflowExecutionLog or WorkflowExecutionStep)
            .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>
    /// The lead's name as it was when the rule ran. Snapshotted so a row stays readable after
    /// the lead is renamed, merged or deleted; falls back to the email, then the id, so the
    /// column is never blank.
    /// </summary>
    private static string LeadDisplayName(Lead lead)
    {
        var name = $"{lead.FirstName} {lead.LastName}".Trim();
        if (!string.IsNullOrWhiteSpace(name)) return Cap(name);
        if (!string.IsNullOrWhiteSpace(lead.Email)) return Cap(lead.Email);
        return lead.Id.ToString();
    }

    private static string Cap(string value) => value.Length <= 400 ? value : value[..400];
}
