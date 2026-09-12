using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Workflow.Execution;

/// <summary>
/// Recent-run counts and the latest outcome per rule, for the workflow rules list.
///
/// One grouped query for every rule rather than a per-row fetch: the rules list renders all
/// rules at once, so the alternative is N requests that each cost a round trip and together
/// make the page's load time scale with the number of rules.
/// </summary>
public class GetWorkflowRuleRunSummaryEndpoint : IEndpoint
{
    /// <summary>
    /// Window the counts cover. "Recent" has to mean something specific or the number is not
    /// comparable between rules; 7 days matches how often an Admin checks an automation.
    /// </summary>
    private const int WindowDays = 7;

    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/workflow-rules/run-summary", Handle)
        .WithSummary("Recent run counts and latest status per workflow rule")
        .WithTags("Workflow")
        .RequireAuthorization(PermissionConstants.WorkflowLogsRead);

    public record RuleRunSummary(
        Guid WorkflowRuleId,
        int RecentRunCount,
        int RecentFailureCount,
        string? LatestStatus,
        DateTime? LatestRunAt);

    public record RunSummaryResponse(List<RuleRunSummary> Rules, int WindowDays);

    internal static async Task<Ok<RunSummaryResponse>> Handle(
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var since = timeProvider.GetUtcNow().UtcDateTime.AddDays(-WindowDays);

        var grouped = await db.WorkflowExecutionLogs
            .AsNoTracking()
            .Where(l => l.StartedAt >= since)
            .GroupBy(l => l.WorkflowRuleId)
            .Select(g => new
            {
                WorkflowRuleId = g.Key,
                RecentRunCount = g.Count(),
                // Abandoned counts as a failure for this badge: from the Admin's side a run
                // that never reported an outcome is as much a problem as one that failed.
                RecentFailureCount = g.Count(l =>
                    l.Status == WorkflowExecutionStatus.Failed
                    || l.Status == WorkflowExecutionStatus.PartiallySucceeded
                    || l.Status == WorkflowExecutionStatus.Abandoned),
                LatestRunAt = g.Max(l => l.StartedAt)
            })
            .ToListAsync(cancellationToken);

        // The latest row's status cannot come from the group above — an aggregate cannot carry
        // the status of the row that produced Max(StartedAt) — so it is a second pass keyed by
        // (rule, time), which the (TenantId, RuleId, StartedAt DESC) index already serves.
        var latestKeys = grouped.Select(g => new { g.WorkflowRuleId, g.LatestRunAt }).ToList();
        var ruleIds = latestKeys.Select(k => k.WorkflowRuleId).ToList();

        var latestRows = await db.WorkflowExecutionLogs
            .AsNoTracking()
            .Where(l => ruleIds.Contains(l.WorkflowRuleId) && l.StartedAt >= since)
            .Select(l => new { l.WorkflowRuleId, l.StartedAt, l.Status })
            .ToListAsync(cancellationToken);

        var latestByRule = latestRows
            .GroupBy(r => r.WorkflowRuleId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.StartedAt).First().Status.ToString());

        var summaries = grouped
            .Select(g => new RuleRunSummary(
                g.WorkflowRuleId,
                g.RecentRunCount,
                g.RecentFailureCount,
                latestByRule.GetValueOrDefault(g.WorkflowRuleId),
                g.LatestRunAt))
            .ToList();

        return TypedResults.Ok(new RunSummaryResponse(summaries, WindowDays));
    }
}
