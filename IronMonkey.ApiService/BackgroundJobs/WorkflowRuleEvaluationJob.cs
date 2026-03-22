using Hangfire;
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
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Evaluating workflow rules for lead {LeadId}, trigger {Trigger}", leadId, trigger);

        var connectionString = await tenantRegistry.GetConnectionStringAsync(tenantId, cancellationToken);
        await using var db = tenantContextFactory.CreateForTenant(connectionString, tenantId);

        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
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

        await ruleEngine.EvaluateAsync(tenantId, lead, workflowTrigger, db, cancellationToken);
        logger.LogInformation("Completed workflow rule evaluation for lead {LeadId}", leadId);
    }
}
