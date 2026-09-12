using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

/// <summary>
/// Outcome of one logical workflow-rule execution.
///
/// <see cref="Skipped"/> is an intentional, successful outcome — the rule's condition did
/// not match — and is deliberately distinct from <see cref="Failed"/>. Reporting a mismatch
/// as an error would make every narrow rule look broken.
/// </summary>
public enum WorkflowExecutionStatus
{
    /// <summary>Enqueued on Hangfire, not yet picked up by a worker.</summary>
    Queued = 0,

    /// <summary>A worker has claimed it and is evaluating.</summary>
    Running = 1,

    /// <summary>The condition did not match. Nothing ran, and nothing is wrong.</summary>
    Skipped = 2,

    /// <summary>The condition matched and every action completed.</summary>
    Succeeded = 3,

    /// <summary>The condition matched and every action failed.</summary>
    Failed = 4,

    /// <summary>Some actions completed and some failed.</summary>
    PartiallySucceeded = 5,

    /// <summary>
    /// Left <see cref="Running"/> past the staleness threshold — the worker was killed, the
    /// process recycled, or the job cancelled — and reconciled by
    /// <c>WorkflowExecutionMaintenanceJob</c>. Distinct from Failed: we do not know whether
    /// the actions ran.
    /// </summary>
    Abandoned = 6
}

/// <summary>
/// Why an execution or step did not succeed, in categories an Admin can act on.
///
/// The category is what the UI filters and groups by; <see cref="WorkflowExecutionLog.ErrorMessage"/>
/// carries the redacted detail. Keeping them separate means a new message wording never
/// breaks a saved filter.
/// </summary>
public enum WorkflowErrorCategory
{
    None = 0,

    /// <summary>The rule's ConditionJson could not be parsed.</summary>
    InvalidConditionJson = 1,

    /// <summary>The rule's ActionJson could not be parsed.</summary>
    InvalidActionJson = 2,

    /// <summary>ActionJson named a "type" the engine has no handler for.</summary>
    UnknownActionType = 3,

    /// <summary>ActionJson was parseable but missing or malformed for its type.</summary>
    InvalidActionConfiguration = 4,

    /// <summary>A webhook action's url was absent, relative, or not http(s).</summary>
    InvalidWebhookUrl = 5,

    /// <summary>The webhook responded with a non-2xx status.</summary>
    WebhookNonSuccessResponse = 6,

    /// <summary>The webhook request timed out or the request was cancelled.</summary>
    WebhookTimeout = 7,

    /// <summary>The webhook host could not be reached.</summary>
    WebhookNetworkFailure = 8,

    /// <summary>An email action resolved to no recipient (e.g. "to":"lead" on a lead with no email).</summary>
    MissingEmailRecipient = 9,

    /// <summary>Email delivery is not configured, so the message was recorded but not sent.</summary>
    EmailDeliveryNotConfigured = 10,

    /// <summary>The action referenced a user, stage or record that does not exist in this tenant.</summary>
    ReferencedRecordNotFound = 11,

    /// <summary>Writing the action's effect to the tenant database failed.</summary>
    PersistenceFailure = 12,

    /// <summary>The worker was cancelled mid-execution.</summary>
    Cancelled = 13,

    /// <summary>Anything the engine did not anticipate.</summary>
    UnexpectedError = 99
}

/// <summary>
/// The durable, tenant-visible record of one workflow rule firing against one lead.
///
/// This is the business audit history, not the operator diagnostic: it lives in the tenant
/// database so it inherits database-per-tenant isolation and the global TenantId query
/// filter, and it holds only values already visible to a tenant Admin. Structured
/// <c>ILogger</c> events are still written alongside for operators — the two layers answer
/// different questions and neither replaces the other.
///
/// Rule name and action types are stored as snapshots rather than read through the FK, so a
/// row stays readable after the rule is renamed or deleted. <see cref="WorkflowRuleId"/> is
/// deliberately not a foreign key for the same reason.
/// </summary>
public sealed class WorkflowExecutionLog : BaseTenantEntity
{
    private readonly List<WorkflowExecutionStep> _steps = new();

    private WorkflowExecutionLog() { }

    /// <summary>The rule that fired. Not an FK — the rule may be deleted while history remains.</summary>
    public Guid WorkflowRuleId { get; private set; }

    /// <summary>The rule's name when it ran, so a later rename does not rewrite history.</summary>
    public string WorkflowRuleName { get; private set; } = string.Empty;

    /// <summary>The lead that triggered evaluation. Not an FK, for the same reason as the rule.</summary>
    public Guid LeadId { get; private set; }

    /// <summary>The lead's display name when it ran, so the row reads without a join.</summary>
    public string LeadName { get; private set; } = string.Empty;

    public WorkflowTrigger Trigger { get; private set; }

    public WorkflowExecutionStatus Status { get; private set; } = WorkflowExecutionStatus.Queued;

    public DateTime StartedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    /// <summary>
    /// Wall-clock duration, stored rather than computed so the list can sort and filter on it
    /// in SQL without expressing <c>CompletedAt - StartedAt</c> in every query.
    /// </summary>
    public int? DurationMs { get; private set; }

    public WorkflowErrorCategory ErrorCategory { get; private set; } = WorkflowErrorCategory.None;

    /// <summary>
    /// Human-readable failure summary, already redacted. Everything written here passes
    /// through <c>WorkflowDiagnosticRedactor</c>, so it never carries a secret, a query
    /// string, or a full custom-field payload.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Why the run was skipped, e.g. "condition did not match". Separate from
    /// <see cref="ErrorMessage"/> so the UI can present a skip as the non-event it is.
    /// </summary>
    public string? SkipReason { get; private set; }

    /// <summary>
    /// Stable identity for one logical trigger: same tenant, lead, rule and dispatch.
    /// A Hangfire retry reuses it, which is what makes <see cref="Attempt"/> meaningful and
    /// keeps three retries of one trigger from reading as three separate runs.
    /// </summary>
    public string CorrelationId { get; private set; } = string.Empty;

    /// <summary>The Hangfire job id, for matching a row against operator logs and the dashboard.</summary>
    public string? JobId { get; private set; }

    /// <summary>1 on first execution, incremented by each Hangfire retry of the same trigger.</summary>
    public int Attempt { get; private set; } = 1;

    /// <summary>True when the condition was evaluated without throwing.</summary>
    public bool ConditionEvaluated { get; private set; }

    /// <summary>True when the condition matched and the actions were therefore attempted.</summary>
    public bool ConditionMatched { get; private set; }

    public IReadOnlyList<WorkflowExecutionStep> Steps => _steps;

    /// <summary>
    /// Opens a record in <see cref="WorkflowExecutionStatus.Running"/>. The row is written
    /// before any action runs, so a worker killed mid-execution still leaves evidence that
    /// the trigger was received — which the reconciliation job can then mark Abandoned.
    /// </summary>
    public static WorkflowExecutionLog Start(
        Guid tenantId,
        WorkflowRule rule,
        Guid leadId,
        string leadName,
        WorkflowTrigger trigger,
        string correlationId,
        string? jobId,
        int attempt,
        DateTime startedAt)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkflowRuleId = rule.Id,
            WorkflowRuleName = rule.Name,
            LeadId = leadId,
            LeadName = leadName,
            Trigger = trigger,
            Status = WorkflowExecutionStatus.Running,
            StartedAt = startedAt,
            CorrelationId = correlationId,
            JobId = jobId,
            Attempt = attempt < 1 ? 1 : attempt
        };

    public void RecordConditionResult(bool evaluated, bool matched)
    {
        ConditionEvaluated = evaluated;
        ConditionMatched = matched;
    }

    public WorkflowExecutionStep AddStep(WorkflowExecutionStep step)
    {
        _steps.Add(step);
        return step;
    }

    /// <summary>Closes the record as an intentional skip. Not a failure, so no error category.</summary>
    public void CompleteSkipped(string reason, DateTime completedAt)
    {
        Status = WorkflowExecutionStatus.Skipped;
        SkipReason = reason;
        Finish(completedAt);
    }

    /// <summary>
    /// Closes the record from the steps actually recorded, so the overall status can never
    /// disagree with the timeline below it. An execution whose only action failed is Failed;
    /// a mix is PartiallySucceeded.
    /// </summary>
    public void CompleteFromSteps(DateTime completedAt)
    {
        // Action steps only. The condition step is always Succeeded on this path — the
        // condition matched, which is why we are executing actions at all — so counting it
        // would make a run whose single action failed read as PartiallySucceeded.
        var attempted = _steps
            .Where(s => s.Kind == WorkflowStepKind.Action
                        && s.Status is WorkflowStepStatus.Succeeded or WorkflowStepStatus.Failed)
            .ToList();

        if (attempted.Count == 0)
        {
            // The condition matched but nothing ran — a rule with an empty action list.
            Status = WorkflowExecutionStatus.Succeeded;
        }
        else
        {
            var failed = attempted.Count(s => s.Status == WorkflowStepStatus.Failed);
            Status = failed switch
            {
                0 => WorkflowExecutionStatus.Succeeded,
                var f when f == attempted.Count => WorkflowExecutionStatus.Failed,
                _ => WorkflowExecutionStatus.PartiallySucceeded
            };
        }

        // Surface the first failure at the top level so the list needs no join to explain
        // itself. Steps keep the full per-action detail.
        var firstFailure = _steps.FirstOrDefault(s =>
            s.Kind == WorkflowStepKind.Action && s.Status == WorkflowStepStatus.Failed);
        if (firstFailure is not null)
        {
            ErrorCategory = firstFailure.ErrorCategory;
            ErrorMessage = firstFailure.Message;
        }

        Finish(completedAt);
    }

    /// <summary>
    /// Closes the record as a failure that happened outside any action — malformed condition
    /// JSON, or an exception before the first step was opened.
    /// </summary>
    public void CompleteFailed(WorkflowErrorCategory category, string message, DateTime completedAt)
    {
        Status = WorkflowExecutionStatus.Failed;
        ErrorCategory = category;
        ErrorMessage = message;
        Finish(completedAt);
    }

    /// <summary>
    /// Marks a row that was left Running past the staleness threshold. Used only by the
    /// reconciliation job: the actions may or may not have run, which is exactly what
    /// Abandoned means and why it is not Failed.
    /// </summary>
    public void MarkAbandoned(string reason, DateTime completedAt)
    {
        Status = WorkflowExecutionStatus.Abandoned;
        ErrorCategory = WorkflowErrorCategory.Cancelled;
        ErrorMessage = reason;
        Finish(completedAt);
    }

    private void Finish(DateTime completedAt)
    {
        CompletedAt = completedAt;
        // Clamped at zero: a sub-millisecond run and a clock that steps backwards must not
        // persist a negative duration the UI would render as "-3ms".
        DurationMs = (int)Math.Max(0, (completedAt - StartedAt).TotalMilliseconds);
    }
}
