using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public enum WorkflowStepStatus
{
    Running = 0,

    /// <summary>The action completed as configured.</summary>
    Succeeded = 1,

    /// <summary>The action was attempted and did not complete.</summary>
    Failed = 2,

    /// <summary>The action was not attempted — the condition it belonged to did not match.</summary>
    Skipped = 3
}

/// <summary>
/// What kind of step a timeline row represents.
///
/// The condition check is a step of its own rather than a pair of columns on the execution,
/// so the detail view is one ordered list — trigger received, condition evaluated, each
/// action started and finished — instead of a header the reader must merge with a table.
/// </summary>
public enum WorkflowStepKind
{
    /// <summary>The rule's condition was evaluated.</summary>
    Condition = 0,

    /// <summary>One action from the rule's ActionJson ran.</summary>
    Action = 1
}

/// <summary>
/// One entry in an execution's timeline.
///
/// Action type is a snapshot string, not an enum: the engine must be able to record that a
/// rule asked for an action type it does not support, and an enum has no member for an
/// unknown value. <see cref="WorkflowExecutionLog"/> owns these; they are never queried
/// independently of their parent.
/// </summary>
public sealed class WorkflowExecutionStep : BaseTenantEntity
{
    private WorkflowExecutionStep() { }

    public Guid WorkflowExecutionLogId { get; private set; }

    /// <summary>Position in the timeline, 1-based. Ordering key — two steps can share a timestamp.</summary>
    public int Sequence { get; private set; }

    public WorkflowStepKind Kind { get; private set; }

    /// <summary>
    /// The action's declared "type" as the rule wrote it, e.g. "webhook" — or whatever
    /// unrecognised string the rule carried, so an unknown type is diagnosable. Null for a
    /// condition step.
    /// </summary>
    public string? ActionType { get; private set; }

    public WorkflowStepStatus Status { get; private set; } = WorkflowStepStatus.Running;

    public DateTime StartedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public int? DurationMs { get; private set; }

    public WorkflowErrorCategory ErrorCategory { get; private set; } = WorkflowErrorCategory.None;

    /// <summary>
    /// Safe, already-redacted diagnostic. For a success this is what happened ("POST to
    /// hooks.example.com returned 200"); for a failure, why it did not.
    /// </summary>
    public string? Message { get; private set; }

    /// <summary>HTTP status for a webhook step, so success and 4xx/5xx are distinguishable without parsing the message.</summary>
    public int? HttpStatusCode { get; private set; }

    /// <summary>
    /// The webhook's scheme, host and path with the query string stripped — a query string
    /// is a common place for a shared secret or signed token.
    /// </summary>
    public string? TargetHost { get; private set; }

    /// <summary>
    /// An email recipient masked to its shape, e.g. <c>j***@example.com</c>. The domain is
    /// kept because it is what makes a misrouted rule diagnosable; the local part is not.
    /// </summary>
    public string? RecipientRedacted { get; private set; }

    public WorkflowExecutionLog? ExecutionLog { get; private set; }

    public static WorkflowExecutionStep Start(
        Guid tenantId,
        Guid executionLogId,
        int sequence,
        WorkflowStepKind kind,
        string? actionType,
        DateTime startedAt)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkflowExecutionLogId = executionLogId,
            Sequence = sequence,
            Kind = kind,
            ActionType = actionType,
            Status = WorkflowStepStatus.Running,
            StartedAt = startedAt
        };

    public void Succeed(string? message, DateTime completedAt)
    {
        Status = WorkflowStepStatus.Succeeded;
        Message = message;
        Finish(completedAt);
    }

    public void Fail(WorkflowErrorCategory category, string message, DateTime completedAt)
    {
        Status = WorkflowStepStatus.Failed;
        ErrorCategory = category;
        Message = message;
        Finish(completedAt);
    }

    /// <summary>Records that the step was deliberately not attempted.</summary>
    public void Skip(string reason, DateTime completedAt)
    {
        Status = WorkflowStepStatus.Skipped;
        Message = reason;
        Finish(completedAt);
    }

    public void RecordHttpResult(int statusCode, string? targetHost)
    {
        HttpStatusCode = statusCode;
        TargetHost = targetHost;
    }

    public void RecordTargetHost(string? targetHost) => TargetHost = targetHost;

    public void RecordRecipient(string? recipientRedacted) => RecipientRedacted = recipientRedacted;

    private void Finish(DateTime completedAt)
    {
        CompletedAt = completedAt;
        DurationMs = (int)Math.Max(0, (completedAt - StartedAt).TotalMilliseconds);
    }
}
