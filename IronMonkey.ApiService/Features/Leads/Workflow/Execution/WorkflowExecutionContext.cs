using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Workflow.Execution;

/// <summary>
/// Identifies one logical trigger across retries.
///
/// <see cref="CorrelationId"/> is derived from the trigger, not generated per attempt: a
/// Hangfire retry of the same job produces the same value, which is what lets the recorder
/// recognise a repeat and link the attempts instead of writing an unrelated second run.
/// <see cref="Attempt"/> comes from Hangfire's retry count, so each attempt is its own row
/// under one correlation id — a retry is visible as a retry rather than overwriting whatever
/// the failed attempt managed to record.
/// </summary>
public sealed record WorkflowExecutionContext(
    Guid TenantId,
    string CorrelationId,
    string? JobId,
    int Attempt)
{
    /// <summary>
    /// Builds the correlation id for a lead-triggered evaluation.
    ///
    /// Deliberately derived from (tenant, lead, trigger, jobId) rather than being random.
    /// The Hangfire job id is stable across that job's retries, so all attempts of one
    /// dispatch agree on it; two separate dispatches for the same lead and trigger are
    /// different jobs and so correctly read as different runs.
    /// </summary>
    public static string CorrelationFor(Guid leadId, WorkflowTrigger trigger, string? jobId)
    {
        // No job id means the evaluation was not dispatched through Hangfire (a direct call,
        // or storage being unavailable). A random suffix keeps such a run from colliding with
        // another on the unique (tenant, correlation, attempt) index.
        var scope = string.IsNullOrWhiteSpace(jobId) ? Guid.NewGuid().ToString("N")[..12] : jobId;
        return $"{leadId:N}:{trigger}:{scope}";
    }

    /// <summary>
    /// Context for an evaluation that is not running under Hangfire — the time-elapsed scan
    /// evaluating many leads in one job, or a test calling the engine directly. Each lead
    /// gets its own correlation id because each is its own logical run.
    /// </summary>
    public static WorkflowExecutionContext ForDirectCall(Guid tenantId, Guid leadId, WorkflowTrigger trigger, string? jobId = null)
        => new(tenantId, CorrelationFor(leadId, trigger, jobId), jobId, Attempt: 1);
}
