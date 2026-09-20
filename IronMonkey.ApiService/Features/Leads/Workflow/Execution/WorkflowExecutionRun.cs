using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Workflow;

namespace IronMonkey.ApiService.Features.Leads.Workflow.Execution;

/// <summary>
/// A live handle on one execution row, driven by the engine as the rule progresses.
///
/// Every method here is swallow-on-failure by design: the engine calls these from inside the
/// action it is already performing, and an audit write that throws would roll back or abort
/// the business effect the audit exists to describe. Failures go to <c>ILogger</c>, which is
/// the right home for "the history table is broken" — an operator problem, not a tenant one.
///
/// Redaction happens here rather than at the call sites so no caller can forget: every
/// message, URL, host and recipient that reaches the entity has passed through
/// <see cref="WorkflowDiagnosticRedactor"/>.
/// </summary>
public sealed class WorkflowExecutionRun(
    TenantDbContext db,
    WorkflowExecutionLog log,
    ILogger logger,
    TimeProvider timeProvider)
{
    private int _sequence;
    private WorkflowExecutionStep? _currentStep;

    public Guid ExecutionId => log.Id;
    public Guid RuleId => log.WorkflowRuleId;
    public string CorrelationId => log.CorrelationId;
    public int Attempt => log.Attempt;

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Records that the condition was evaluated and matched. The condition is a timeline step
    /// of its own so the detail view reads in order rather than as a header plus a table.
    /// </summary>
    public Task ConditionMatchedAsync(string summary, CancellationToken cancellationToken)
        => RecordConditionAsync(evaluated: true, matched: true, summary, category: null, cancellationToken);

    /// <summary>Records an intentional skip and closes the run. Not an error.</summary>
    public async Task ConditionNotMatchedAsync(string reason, CancellationToken cancellationToken)
    {
        await RecordConditionAsync(evaluated: true, matched: false, reason, category: null, cancellationToken);

        await SaveAsync(() =>
        {
            log.CompleteSkipped(Truncate(reason, 300) ?? "Condition did not match.", UtcNow);
        }, cancellationToken);
    }

    /// <summary>
    /// Records that the condition could not be evaluated — malformed JSON — and closes the run
    /// as failed. Distinct from "did not match": the rule is broken, not merely inapplicable.
    /// </summary>
    public async Task ConditionFailedAsync(string message, CancellationToken cancellationToken)
    {
        var safe = WorkflowDiagnosticRedactor.Redact(message) ?? "Condition could not be evaluated.";
        await RecordConditionAsync(evaluated: false, matched: false, safe,
            WorkflowErrorCategory.InvalidConditionJson, cancellationToken);

        await SaveAsync(() =>
        {
            log.CompleteFailed(WorkflowErrorCategory.InvalidConditionJson, safe, UtcNow);
        }, cancellationToken);
    }

    /// <summary>
    /// Opens an action step. The step is persisted as <c>Running</c> before the action is
    /// attempted, so an action that kills the worker — a hung webhook, a process recycle —
    /// still leaves a record of having been started.
    /// </summary>
    public async Task BeginActionAsync(string? actionType, CancellationToken cancellationToken)
    {
        var step = WorkflowExecutionStep.Start(
            log.TenantId, log.Id, ++_sequence, WorkflowStepKind.Action,
            Truncate(actionType, 60), UtcNow);

        _currentStep = step;

        await SaveAsync(() =>
        {
            log.AddStep(step);
            db.WorkflowExecutionSteps.Add(step);
        }, cancellationToken);
    }

    public Task ActionSucceededAsync(string? message, CancellationToken cancellationToken)
        => CloseStepAsync(step => step.Succeed(WorkflowDiagnosticRedactor.Redact(message), UtcNow), cancellationToken);

    public Task ActionFailedAsync(WorkflowErrorCategory category, string message, CancellationToken cancellationToken)
        => CloseStepAsync(step => step.Fail(
            category,
            WorkflowDiagnosticRedactor.Redact(message) ?? "Action failed.",
            UtcNow), cancellationToken);

    /// <summary>
    /// Attaches the HTTP outcome to the open webhook step. The host is stored without path or
    /// query so the column can group runs by destination without carrying a signed token.
    /// </summary>
    public void RecordHttpResult(int statusCode, Uri? uri)
        => _currentStep?.RecordHttpResult(statusCode, WorkflowDiagnosticRedactor.SafeHost(uri));

    public void RecordTargetHost(Uri? uri)
        => _currentStep?.RecordTargetHost(WorkflowDiagnosticRedactor.SafeHost(uri));

    /// <summary>Attaches a masked email recipient, e.g. <c>j***@example.com</c>.</summary>
    public void RecordRecipient(string? recipient)
        => _currentStep?.RecordRecipient(WorkflowDiagnosticRedactor.MaskEmail(recipient));

    /// <summary>
    /// Closes the run, deriving the overall status from the steps recorded so the header can
    /// never contradict the timeline beneath it.
    /// </summary>
    public Task CompleteAsync(CancellationToken cancellationToken)
        => SaveAsync(() => log.CompleteFromSteps(UtcNow), cancellationToken);

    /// <summary>
    /// Closes the run after an exception that escaped the action handling — the engine's
    /// outer catch. If a step was still open it is failed too, so no row is left Running and
    /// the reconciliation job has nothing to clean up.
    /// </summary>
    public async Task FailAsync(WorkflowErrorCategory category, string message, CancellationToken cancellationToken)
    {
        var safe = WorkflowDiagnosticRedactor.Redact(message) ?? "Execution failed.";

        await SaveAsync(() =>
        {
            if (_currentStep is { Status: WorkflowStepStatus.Running })
                _currentStep.Fail(category, safe, UtcNow);

            log.CompleteFailed(category, safe, UtcNow);
        }, cancellationToken);
    }

    private async Task RecordConditionAsync(
        bool evaluated, bool matched, string summary,
        WorkflowErrorCategory? category, CancellationToken cancellationToken)
    {
        var step = WorkflowExecutionStep.Start(
            log.TenantId, log.Id, ++_sequence, WorkflowStepKind.Condition, actionType: null, UtcNow);

        if (category is { } failed)
            step.Fail(failed, summary, UtcNow);
        else if (matched)
            step.Succeed(summary, UtcNow);
        else
            step.Skip(summary, UtcNow);

        await SaveAsync(() =>
        {
            log.RecordConditionResult(evaluated, matched);
            log.AddStep(step);
            db.WorkflowExecutionSteps.Add(step);
        }, cancellationToken);
    }

    private Task CloseStepAsync(Action<WorkflowExecutionStep> close, CancellationToken cancellationToken)
    {
        if (_currentStep is null) return Task.CompletedTask;

        var step = _currentStep;
        return SaveAsync(() => close(step), cancellationToken);
    }

    /// <summary>
    /// Applies a mutation and flushes it.
    ///
    /// <c>CancellationToken.None</c> is passed to SaveChanges on purpose: when the worker is
    /// being cancelled, the partial history recorded so far is precisely what we want to keep,
    /// and honouring the token here would discard the record of whatever already ran.
    /// </summary>
    private async Task SaveAsync(Action mutate, CancellationToken cancellationToken)
    {
        try
        {
            mutate();
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // An audit write failing must not change what the workflow did, so this is logged
            // for operators and not rethrown. The run continues unrecorded from here.
            logger.LogError(ex,
                "Failed to persist workflow execution history for execution {ExecutionId} " +
                "(rule {RuleId}, tenant {TenantId}, correlation {CorrelationId})",
                log.Id, log.WorkflowRuleId, log.TenantId, log.CorrelationId);
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
