using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Workflow.Execution;

/// <summary>
/// The tenant's workflow run history, newest first, filtered and paged.
///
/// Tenancy is not a parameter: the tenant comes from the authenticated context via
/// <see cref="ITenantService"/>, and every row read is additionally constrained by
/// TenantDbContext's global query filter. A caller cannot widen the scope by asking.
/// </summary>
public class ListWorkflowExecutionsEndpoint : IEndpoint
{
    private const int DefaultPageSize = 25;

    /// <summary>Ceiling on pageSize — without it a caller can ask for the whole table.</summary>
    private const int MaxPageSize = 100;

    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/workflow-executions", Handle)
        .WithSummary("List workflow rule execution history for the current tenant")
        .WithTags("Workflow")
        .RequireAuthorization(PermissionConstants.WorkflowLogsRead);

    public record ExecutionItem(
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
        int Attempt,
        string CorrelationId,
        /// <summary>Action types this run attempted, so the list row says what it tried to do.</summary>
        List<string> ActionTypes);

    public record ExecutionPage(
        List<ExecutionItem> Items,
        int TotalCount,
        int Page,
        int PageSize,
        int TotalPages,
        /// <summary>
        /// The oldest row retention still holds, so the UI can say "older runs have been
        /// removed" rather than letting an empty older page read as "this never ran".
        /// </summary>
        DateTime? OldestRetainedAt,
        int RetentionDays);

    internal static async Task<Ok<ExecutionPage>> Handle(
        DateTime? from,
        DateTime? to,
        string? status,
        Guid? ruleId,
        string? trigger,
        Guid? leadId,
        string? actionType,
        int? page,
        int? pageSize,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        Microsoft.Extensions.Options.IOptions<WorkflowExecutionLogOptions> options,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var query = db.WorkflowExecutionLogs.AsQueryable();

        // Half-open [from, toExclusive) for the same reason the dashboard uses it: an
        // inclusive end-of-day bound cannot be expressed exactly against a timestamptz and
        // silently drops rows in the final fraction of a second.
        if (from.HasValue)
            query = query.Where(l => l.StartedAt >= DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc));

        if (to.HasValue)
        {
            var toExclusive = DateTime.SpecifyKind(to.Value.Date, DateTimeKind.Utc).AddDays(1);
            query = query.Where(l => l.StartedAt < toExclusive);
        }

        if (Enum.TryParse<WorkflowExecutionStatus>(status, ignoreCase: true, out var parsedStatus))
            query = query.Where(l => l.Status == parsedStatus);

        if (Enum.TryParse<WorkflowTrigger>(trigger, ignoreCase: true, out var parsedTrigger))
            query = query.Where(l => l.Trigger == parsedTrigger);

        if (ruleId.HasValue)
            query = query.Where(l => l.WorkflowRuleId == ruleId.Value);

        if (leadId.HasValue)
            query = query.Where(l => l.LeadId == leadId.Value);

        if (!string.IsNullOrWhiteSpace(actionType))
        {
            // Action type lives on the steps, so this filters the parent by existence rather
            // than joining and de-duplicating — a run with two webhook steps must still appear
            // once.
            var wanted = actionType.Trim();
            query = query.Where(l => db.WorkflowExecutionSteps
                .Any(s => s.WorkflowExecutionLogId == l.Id && s.ActionType == wanted));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)size);

        // A page past the end clamps to the last real page rather than returning an empty
        // list that reads as "no runs".
        var requested = Math.Max(page ?? 1, 1);
        var current = totalPages == 0 ? 1 : Math.Min(requested, totalPages);

        var rows = await query
            // Tiebreak on Id: rows sharing a StartedAt have no defined order otherwise, so a
            // run could appear on two pages or on none.
            .OrderByDescending(l => l.StartedAt)
            .ThenByDescending(l => l.Id)
            .Skip((current - 1) * size)
            .Take(size)
            .Select(l => new
            {
                l.Id,
                l.WorkflowRuleId,
                l.WorkflowRuleName,
                l.LeadId,
                l.LeadName,
                l.Trigger,
                l.Status,
                l.StartedAt,
                l.CompletedAt,
                l.DurationMs,
                l.ErrorCategory,
                l.ErrorMessage,
                l.SkipReason,
                l.ConditionEvaluated,
                l.ConditionMatched,
                l.Attempt,
                l.CorrelationId,
                ActionTypes = db.WorkflowExecutionSteps
                    .Where(s => s.WorkflowExecutionLogId == l.Id
                                && s.Kind == WorkflowStepKind.Action
                                && s.ActionType != null)
                    .Select(s => s.ActionType!)
                    .Distinct()
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(l => new ExecutionItem(
            l.Id, l.WorkflowRuleId, l.WorkflowRuleName, l.LeadId, l.LeadName,
            l.Trigger.ToString(), l.Status.ToString(), l.StartedAt, l.CompletedAt, l.DurationMs,
            l.ErrorCategory.ToString(), l.ErrorMessage, l.SkipReason,
            l.ConditionEvaluated, l.ConditionMatched, l.Attempt, l.CorrelationId,
            l.ActionTypes)).ToList();

        // Read from the data rather than computed from the retention window: it answers "what
        // is actually still here", which is what the UI needs to caption the empty state.
        var oldestRetainedAt = await db.WorkflowExecutionLogs
            .OrderBy(l => l.StartedAt)
            .Select(l => (DateTime?)l.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return TypedResults.Ok(new ExecutionPage(
            items, totalCount, current, size, totalPages,
            oldestRetainedAt, options.Value.EffectiveRetentionDays));
    }
}
