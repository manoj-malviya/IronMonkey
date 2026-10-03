using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Pipelines;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using TaskStatus = IronMonkey.Data.Entities.TaskStatus;

namespace IronMonkey.ApiService.Features.Reports.Dashboard;

/// <summary>
/// The dashboard's actionable panel: overdue follow-up tasks and leads that have gone
/// stale, each capped to a handful of rows with a total count so the UI can say
/// "showing 5 of 23" and link to the full list.
///
/// Deliberately NOT date-range filtered. An overdue task is overdue regardless of which
/// window the user is inspecting, and hiding it because it was created outside the range
/// would make the panel actively misleading.
/// </summary>
public class GetDashboardAttentionEndpoint : IEndpoint
{
    /// <summary>Rows returned per section — enough to act on, small enough to scan.</summary>
    private const int SectionLimit = 5;

    /// <summary>A lead untouched for this long counts as stale.</summary>
    private const int StaleAfterDays = 14;

    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/dashboard/attention", Handle)
        .WithSummary("Overdue follow-ups and stale leads needing attention")
        .WithTags("Dashboard")
        .RequireAuthorization();

    public record OverdueTaskItem(
        Guid TaskId,
        Guid LeadId,
        string LeadName,
        string Title,
        DateTime DueDate,
        int DaysOverdue,
        string Priority);

    public record StaleLeadItem(
        Guid LeadId,
        string LeadName,
        string StageName,
        DateTime LastActivityAt,
        int DaysSinceActivity);

    public record DashboardAttentionResponse(
        int OverdueTaskCount,
        List<OverdueTaskItem> OverdueTasks,
        int StaleLeadCount,
        List<StaleLeadItem> StaleLeads,
        int StaleAfterDays,
        string ScopeLabel,
        Guid? PipelineId,
        bool IsTenantWide,
        bool IsMultiPipeline,
        DateTime GeneratedAt);

    /// <param name="pipelineId">
    /// Which lead pipeline to report on. Omitted means the default one. This panel stays
    /// deliberately un-date-filtered (an overdue task is overdue in any window) but it IS
    /// pipeline-filtered: a stale lead belongs to one funnel, and showing another funnel's
    /// under this one's heading is the same wrong-number problem the date rule is not about.
    /// </param>
    internal static async Task<Results<Ok<DashboardAttentionResponse>, BadRequest<string>>> Handle(
        string? pipelineId,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IPipelineScopeResolver scopeResolver,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var scope = await scopeResolver.ResolveAsync(
            db, PipelineRecordType.Lead, pipelineId, cancellationToken);

        if (!scope.IsValid) return TypedResults.BadRequest(scope.Error!);

        var scopedPipelines = scope.PipelineIds;

        var now = DateTime.UtcNow;
        var staleBefore = now.AddDays(-StaleAfterDays);

        // Overdue = past due and still actionable. Completed and cancelled tasks are done
        // with, however far in the past their due date sits.
        // A task belongs to a lead, and the lead belongs to a pipeline — so the task is
        // scoped through its lead. Left unscoped, a tenant's second pipeline's overdue work
        // would be counted into the first pipeline's panel.
        var leadIdsInScope = db.Leads
            .Where(l => scopedPipelines.Contains(l.PipelineId))
            .Select(l => l.Id);

        var overdueQuery = db.LeadTasks
            .Where(t => leadIdsInScope.Contains(t.LeadId))
            .Where(t => t.DueDate != null
                        && t.DueDate < now
                        && t.Status != TaskStatus.Completed
                        && t.Status != TaskStatus.Cancelled);

        var overdueCount = await overdueQuery.CountAsync(cancellationToken);

        var overdueTasks = await overdueQuery
            .OrderBy(t => t.DueDate)
            .Take(SectionLimit)
            .Select(t => new
            {
                t.Id,
                t.LeadId,
                LeadFirst = t.Lead.FirstName,
                LeadLast = t.Lead.LastName,
                t.Title,
                t.DueDate,
                t.Priority
            })
            .ToListAsync(cancellationToken);

        // Stale = open work nobody has touched recently. Converted leads and leads in a
        // terminal stage are finished, so ageing there is expected, not a problem.
        var terminalStageIds = await db.PipelineStages
            .Where(s => (s.StageType == StageType.ClosedWon || s.StageType == StageType.ClosedLost)
                        && scopedPipelines.Contains(s.PipelineId))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        // UpdatedAt is the touch marker: TenantDbContext.GenerateTimestamps stamps it on
        // insert and on every subsequent edit or stage move, so it tracks real work rather
        // than mere age, and is never default for a persisted row.
        var staleQuery = db.Leads
            .Where(l => scopedPipelines.Contains(l.PipelineId))
            .Where(l => !l.IsConverted
                        && !terminalStageIds.Contains(l.PipelineStageId)
                        && l.UpdatedAt < staleBefore);

        var staleCount = await staleQuery.CountAsync(cancellationToken);

        var staleLeads = await staleQuery
            .OrderBy(l => l.UpdatedAt)
            .Take(SectionLimit)
            .Select(l => new
            {
                l.Id,
                l.FirstName,
                l.LastName,
                StageName = l.Stage.Name,
                LastActivityAt = l.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new DashboardAttentionResponse(
            overdueCount,
            overdueTasks.Select(t => new OverdueTaskItem(
                t.Id,
                t.LeadId,
                FormatName(t.LeadFirst, t.LeadLast),
                t.Title,
                t.DueDate!.Value,
                // Whole days late, floored: a task due 20 hours ago reads "today", not "1 day".
                Math.Max(0, (int)(now - t.DueDate!.Value).TotalDays),
                t.Priority.ToString())).ToList(),
            staleCount,
            staleLeads.Select(l => new StaleLeadItem(
                l.Id,
                FormatName(l.FirstName, l.LastName),
                l.StageName,
                l.LastActivityAt,
                Math.Max(0, (int)(now - l.LastActivityAt).TotalDays))).ToList(),
            StaleAfterDays,
            scope.ScopeLabel,
            scope.SelectedPipelineId,
            scope.IsTenantWide,
            scope.IsMultiPipelineTenant,
            now));
    }

    private static string FormatName(string first, string last)
    {
        var name = $"{first} {last}".Trim();
        return string.IsNullOrEmpty(name) ? "(unnamed)" : name;
    }
}
