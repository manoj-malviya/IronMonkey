using Hangfire;
using Hangfire.Server;
using IronMonkey.ApiService.Features.Leads.Workflow.Execution;
using IronMonkey.ApiService.Features.Leads.Workflow.Rules;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IronMonkey.ApiService.BackgroundJobs;

public class WorkflowRuleEvaluationJob(
    ITenantRegistry tenantRegistry,
    ITenantDbContextFactory tenantContextFactory,
    IWorkflowRuleEngine ruleEngine,
    ILogger<WorkflowRuleEvaluationJob> logger)
{
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 300])]
    [Queue("tenant")]
    public async Task ExecuteAsync(Guid tenantId, Guid leadId, string trigger,
        PerformContext? performContext, CancellationToken cancellationToken = default)
    {
        // The Hangfire job id is stable across this job's retries, so it is what ties the
        // attempts of one dispatch together in the execution history. PerformContext is
        // injected by Hangfire at invocation time — it is null when the method is called
        // directly, which tests and the scan job do.
        var jobId = performContext?.BackgroundJob.Id;
        var attempt = GetRetryAttempt(performContext);

        logger.LogDebug(
            "Evaluating workflow rules for lead {LeadId}, trigger {Trigger}, job {JobId}, attempt {Attempt}",
            leadId, trigger, jobId, attempt);

        var connectionString = await tenantRegistry.GetConnectionStringAsync(tenantId, cancellationToken);
        await using var db = tenantContextFactory.CreateForTenant(connectionString, tenantId);

        var lead = await db.Leads
            .Include(l => l.Stage)
            .FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);

        if (lead == null)
        {
            logger.LogWarning("Lead {LeadId} not found for workflow rule evaluation", leadId);
            return;
        }

        if (!Enum.TryParse<WorkflowTrigger>(trigger, ignoreCase: true, out var workflowTrigger))
        {
            logger.LogWarning("Unknown trigger type: {Trigger}", trigger);
            return;
        }

        var context = new WorkflowExecutionContext(
            tenantId,
            WorkflowExecutionContext.CorrelationFor(leadId, workflowTrigger, jobId),
            jobId,
            attempt);

        await ruleEngine.EvaluateAsync(tenantId, lead, workflowTrigger, db, cancellationToken, context);

        logger.LogInformation(
            "Completed workflow rule evaluation for lead {LeadId} (tenant {TenantId}, trigger {Trigger}, correlation {CorrelationId})",
            leadId, tenantId, workflowTrigger, context.CorrelationId);
    }

    /// <summary>
    /// Which attempt this invocation is, 1-based.
    ///
    /// Hangfire stores the count in the job's <c>RetryCount</c> parameter and only writes it
    /// once a retry has been scheduled, so its absence means "first attempt" rather than
    /// "unknown". The value is JSON-encoded, hence the trimmed quotes.
    /// </summary>
    private static int GetRetryAttempt(PerformContext? performContext)
    {
        var raw = performContext?.GetJobParameter<string>("RetryCount");
        if (string.IsNullOrWhiteSpace(raw)) return 1;

        return int.TryParse(raw.Trim('"'), out var count) ? count + 1 : 1;
    }
}
