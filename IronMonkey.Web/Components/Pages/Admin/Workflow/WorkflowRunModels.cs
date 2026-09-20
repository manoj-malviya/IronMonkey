namespace IronMonkey.Web.Components.Pages.Admin.Workflow;

/// <summary>
/// Response shapes for the workflow execution history endpoints.
///
/// Deliberately a mirror of the API's records rather than a reference to them: the web project
/// does not reference the API project, and these are the only shapes the pages bind to.
/// </summary>
public sealed class WorkflowRunItem
{
    public Guid Id { get; set; }
    public Guid WorkflowRuleId { get; set; }
    public string WorkflowRuleName { get; set; } = string.Empty;
    public Guid LeadId { get; set; }
    public string LeadName { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? DurationMs { get; set; }
    public string ErrorCategory { get; set; } = "None";
    public string? ErrorMessage { get; set; }
    public string? SkipReason { get; set; }
    public bool ConditionEvaluated { get; set; }
    public bool ConditionMatched { get; set; }
    public int Attempt { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public List<string> ActionTypes { get; set; } = new();
}

public sealed class WorkflowRunPage
{
    public List<WorkflowRunItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }

    /// <summary>Oldest run retention still holds, so the UI can caption an empty older page.</summary>
    public DateTime? OldestRetainedAt { get; set; }
    public int RetentionDays { get; set; }
}

public sealed class WorkflowRunStep
{
    public Guid Id { get; set; }
    public int Sequence { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ActionType { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? DurationMs { get; set; }
    public string ErrorCategory { get; set; } = "None";
    public string? Message { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? TargetHost { get; set; }
    public string? RecipientRedacted { get; set; }
}

public sealed class WorkflowRunAttempt
{
    public Guid Id { get; set; }
    public int Attempt { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public int? DurationMs { get; set; }
}

/// <summary>
/// Named ...Dto because the page component is itself a class called WorkflowRunDetail — a
/// Razor page and its model cannot share a name in the same namespace.
/// </summary>
public sealed class WorkflowRunDetailDto
{
    public Guid Id { get; set; }
    public Guid WorkflowRuleId { get; set; }
    public string WorkflowRuleName { get; set; } = string.Empty;
    public Guid LeadId { get; set; }
    public string LeadName { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? DurationMs { get; set; }
    public string ErrorCategory { get; set; } = "None";
    public string? ErrorMessage { get; set; }
    public string? SkipReason { get; set; }
    public bool ConditionEvaluated { get; set; }
    public bool ConditionMatched { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? JobId { get; set; }
    public int Attempt { get; set; }
    public List<WorkflowRunAttempt> RelatedAttempts { get; set; } = new();
    public List<WorkflowRunStep> Steps { get; set; } = new();
}

public sealed class WorkflowRuleRunSummary
{
    public Guid WorkflowRuleId { get; set; }
    public int RecentRunCount { get; set; }
    public int RecentFailureCount { get; set; }
    public string? LatestStatus { get; set; }
    public DateTime? LatestRunAt { get; set; }
}

public sealed class WorkflowRunSummaryResponse
{
    public List<WorkflowRuleRunSummary> Rules { get; set; } = new();
    public int WindowDays { get; set; }
}

/// <summary>
/// Presentation for an execution status.
///
/// Status is not a simple good/bad axis here: <c>Skipped</c> is a successful, intentional
/// outcome and must not be coloured as a failure, while <c>Abandoned</c> is an unknown rather
/// than a known failure. Centralised so every view agrees on that reading.
/// </summary>
public static class WorkflowRunStatusDisplay
{
    public static string Badge(string? status) => status switch
    {
        "Succeeded" => "bg-emerald-100 text-emerald-800",
        // Slate, not amber: a condition that did not match is a non-event, not a warning.
        "Skipped" => "bg-slate-100 text-slate-700",
        "Failed" => "bg-red-100 text-red-800",
        "PartiallySucceeded" => "bg-amber-100 text-amber-800",
        "Abandoned" => "bg-orange-100 text-orange-800",
        "Running" => "bg-blue-100 text-blue-800",
        "Queued" => "bg-slate-100 text-slate-600",
        _ => "bg-slate-100 text-slate-600"
    };

    public static string Label(string? status) => status switch
    {
        "PartiallySucceeded" => "Partly succeeded",
        null or "" => "—",
        _ => status
    };

    /// <summary>Plain-language explanation shown under the status on the detail page.</summary>
    public static string Explain(string? status) => status switch
    {
        "Succeeded" => "The condition matched and every action completed.",
        "Skipped" => "The rule's condition did not match this lead, so no action ran. This is not an error.",
        "Failed" => "The condition matched but the action did not complete.",
        "PartiallySucceeded" => "Some actions completed and some failed.",
        "Abandoned" => "This run never reported an outcome — the worker stopped before finishing. Actions it had already started may or may not have completed.",
        "Running" => "This run is still in progress.",
        "Queued" => "This run is waiting to be picked up.",
        _ => ""
    };

    /// <summary>
    /// Turns a stored error category into something an Admin can act on. The category is the
    /// stable value; this wording can change without breaking a saved filter.
    /// </summary>
    public static string ExplainCategory(string? category) => category switch
    {
        "InvalidConditionJson" => "The rule's condition is not valid JSON, so it could never match.",
        "InvalidActionJson" => "The rule's action is not valid JSON.",
        "UnknownActionType" => "The rule asks for an action type this system does not support.",
        "InvalidActionConfiguration" => "The action is missing a setting it needs.",
        "InvalidWebhookUrl" => "The webhook address is not a valid http(s) URL.",
        "WebhookNonSuccessResponse" => "The webhook endpoint answered with an error status.",
        "WebhookTimeout" => "The webhook endpoint did not answer in time.",
        "WebhookNetworkFailure" => "The webhook endpoint could not be reached.",
        "MissingEmailRecipient" => "The rule had no address to send to.",
        "EmailDeliveryNotConfigured" => "Email sending is not configured, so nothing was delivered.",
        "ReferencedRecordNotFound" => "The rule refers to a user or record that no longer exists.",
        "PersistenceFailure" => "The change could not be saved.",
        "Cancelled" => "The run was stopped before it finished.",
        "UnexpectedError" => "An unexpected error stopped the run.",
        _ => ""
    };

    /// <summary>Durations read as ms below a second and seconds above, never "1234ms".</summary>
    public static string Duration(int? ms) => ms switch
    {
        null => "—",
        < 1000 => $"{ms}ms",
        < 60_000 => $"{ms.Value / 1000.0:0.0}s",
        _ => $"{ms.Value / 60_000}m {(ms.Value % 60_000) / 1000}s"
    };
}
