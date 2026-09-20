using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Workflow.Execution;

/// <summary>
/// One execution's timeline: the trigger, the condition result, each action, and the outcome.
///
/// Returns only columns that were already redacted on the way in — there is no action payload,
/// no header, no rendered email body and no full webhook URL anywhere in this response, because
/// none of it was persisted. The endpoint does not re-derive anything from the rule either, so
/// a rule edited since the run cannot change what the history reports.
/// </summary>
public class GetWorkflowExecutionEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/workflow-executions/{id:guid}", Handle)
        .WithSummary("Get one workflow execution with its step timeline")
        .WithTags("Workflow")
        .RequireAuthorization(PermissionConstants.WorkflowLogsRead);

    public record StepItem(
        Guid Id,
        int Sequence,
        string Kind,
        string? ActionType,
        string Status,
        DateTime StartedAt,
        DateTime? CompletedAt,
        int? DurationMs,
        string ErrorCategory,
        string? Message,
        int? HttpStatusCode,
        string? TargetHost,
        string? RecipientRedacted);

    public record ExecutionDetail(
        Guid Id,
        Guid WorkflowRuleId,
        string WorkflowRuleName,
        Guid LeadId,
        string LeadName,
        string Trigger,
        string Status,
        DateTime StartedAt,
        DateTime? CompletedAt,
        int? DurationMs,
        string ErrorCategory,
        string? ErrorMessage,
        string? SkipReason,
        bool ConditionEvaluated,
        bool ConditionMatched,
        string CorrelationId,
        string? JobId,
        int Attempt,
        /// <summary>
        /// Other attempts of the same logical trigger, so a retry is readable as a retry
        /// rather than as an unrelated run that happens to look similar.
        /// </summary>
        List<AttemptItem> RelatedAttempts,
        List<StepItem> Steps);

    public record AttemptItem(Guid Id, int Attempt, string Status, DateTime StartedAt, int? DurationMs);

    internal static async Task<Results<Ok<ExecutionDetail>, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        // The global query filter scopes this to the tenant, and the tenant's database is its
        // own — so an id belonging to another tenant is simply absent. The response is the same
        // plain 404 as a genuinely unknown id, with no message distinguishing the two, so a
        // caller cannot probe another tenant for the existence of an execution.
        var log = await db.WorkflowExecutionLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

        if (log is null) return TypedResults.NotFound();

        var steps = await db.WorkflowExecutionSteps
            .AsNoTracking()
            .Where(s => s.WorkflowExecutionLogId == log.Id)
            .OrderBy(s => s.Sequence)
            .Select(s => new StepItem(
                s.Id, s.Sequence, s.Kind.ToString(), s.ActionType, s.Status.ToString(),
                s.StartedAt, s.CompletedAt, s.DurationMs, s.ErrorCategory.ToString(),
                s.Message, s.HttpStatusCode, s.TargetHost, s.RecipientRedacted))
            .ToListAsync(cancellationToken);

        var relatedAttempts = await db.WorkflowExecutionLogs
            .AsNoTracking()
            .Where(l => l.CorrelationId == log.CorrelationId
                        && l.WorkflowRuleId == log.WorkflowRuleId
                        && l.Id != log.Id)
            .OrderBy(l => l.Attempt)
            .Select(l => new AttemptItem(l.Id, l.Attempt, l.Status.ToString(), l.StartedAt, l.DurationMs))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new ExecutionDetail(
            log.Id, log.WorkflowRuleId, log.WorkflowRuleName, log.LeadId, log.LeadName,
            log.Trigger.ToString(), log.Status.ToString(), log.StartedAt, log.CompletedAt,
            log.DurationMs, log.ErrorCategory.ToString(), log.ErrorMessage, log.SkipReason,
            log.ConditionEvaluated, log.ConditionMatched, log.CorrelationId, log.JobId,
            log.Attempt, relatedAttempts, steps));
    }
}
