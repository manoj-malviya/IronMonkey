using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Workflow.Rules;

public interface IWorkflowRuleEngine
{
    /// <summary>
    /// Evaluates all active rules matching the given trigger for the lead.
    /// Fires actions (notify, assign, schedule_task) for rules whose conditions pass.
    /// </summary>
    Task EvaluateAsync(Guid tenantId, Lead lead, WorkflowTrigger trigger,
        TenantDbContext db, CancellationToken cancellationToken);
}
