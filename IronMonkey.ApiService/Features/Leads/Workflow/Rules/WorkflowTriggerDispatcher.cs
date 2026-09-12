using Hangfire;
using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Workflow.Rules;

public interface IWorkflowTriggerDispatcher
{
    void Dispatch(Guid tenantId, Guid leadId, WorkflowTrigger trigger);
}

/// <summary>
/// Enqueues rule evaluation after a lead changes.
///
/// Until 2026-09-10 nothing did this: <see cref="WorkflowRuleEvaluationJob"/> was registered
/// in DI but never enqueued, so FieldChange and StatusChange rules could be created, listed
/// and toggled but never fired. Evaluation runs out-of-band on Hangfire so a slow webhook or
/// a failing rule cannot slow down or roll back the request that triggered it.
/// </summary>
public class WorkflowTriggerDispatcher(
    IBackgroundJobClient backgroundJobs,
    ILogger<WorkflowTriggerDispatcher> logger) : IWorkflowTriggerDispatcher
{
    public void Dispatch(Guid tenantId, Guid leadId, WorkflowTrigger trigger)
    {
        try
        {
            // null PerformContext and CancellationToken.None are placeholders: Hangfire
            // recognises both parameter types and substitutes the live context and the
            // worker's shutdown token when it actually invokes the method. The context is
            // what gives the execution log its job id and retry attempt.
            backgroundJobs.Enqueue<WorkflowRuleEvaluationJob>(
                job => job.ExecuteAsync(tenantId, leadId, trigger.ToString(), null, CancellationToken.None));
        }
        catch (Exception ex)
        {
            // Hangfire storage being unavailable must not fail the caller's write.
            logger.LogError(ex, "Failed to enqueue workflow evaluation for lead {LeadId}", leadId);
        }
    }
}
