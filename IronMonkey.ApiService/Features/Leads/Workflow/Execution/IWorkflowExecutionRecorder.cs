using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Workflow.Execution;

/// <summary>
/// Writes the tenant-visible execution history for a workflow run.
///
/// Separated from <c>WorkflowRuleEngine</c> so the engine keeps one job — deciding what to do
/// — and so recording can be asserted independently in tests. Every method is
/// failure-tolerant by contract: a problem writing history must never change what the
/// workflow did, and must never fail the lead write that triggered it. Implementations
/// therefore swallow their own exceptions after logging them.
/// </summary>
public interface IWorkflowExecutionRecorder
{
    /// <summary>
    /// Opens an execution row in <c>Running</c> and persists it immediately, so evidence that
    /// the trigger was received survives a worker being killed mid-run.
    ///
    /// Returns null when the run must not proceed to recording — either because this exact
    /// (correlation, attempt) was already recorded as finished, which is how a duplicated
    /// Hangfire delivery is suppressed, or because the write failed. A null return is not a
    /// signal to skip the workflow itself: the engine still acts, it just acts unrecorded.
    /// </summary>
    Task<WorkflowExecutionRun?> StartAsync(
        TenantDbContext db,
        WorkflowExecutionContext context,
        WorkflowRule rule,
        Lead lead,
        WorkflowTrigger trigger,
        CancellationToken cancellationToken);
}
