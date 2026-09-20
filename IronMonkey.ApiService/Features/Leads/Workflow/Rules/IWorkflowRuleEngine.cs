using IronMonkey.ApiService.Features.Leads.Workflow.Execution;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Workflow.Rules;

public interface IWorkflowRuleEngine
{
    /// <summary>
    /// Evaluates all active rules matching the given trigger for the lead.
    /// Fires actions (notify, assign, schedule_task, webhook, email) for rules whose
    /// conditions pass, and records a durable, tenant-visible execution row per rule.
    /// </summary>
    /// <param name="context">
    /// Identifies the logical trigger so retries of one dispatch are linked rather than
    /// recorded as separate runs. Null evaluates without writing history — used only where
    /// there is no meaningful correlation to record against.
    /// </param>
    Task EvaluateAsync(Guid tenantId, Lead lead, WorkflowTrigger trigger,
        TenantDbContext db, CancellationToken cancellationToken,
        WorkflowExecutionContext? context = null);
}
