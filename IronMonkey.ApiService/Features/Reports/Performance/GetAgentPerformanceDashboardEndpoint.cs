using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Reports.Performance;

/// <summary>
/// REPT-03: Agent performance dashboard — per-agent metrics for a selected time period.
/// D-15: Leads handled, tasks completed, conversion rate per agent.
/// D-16: Sortable table with: Agent Name, Leads Assigned, Leads Converted, Conversion %, Tasks Completed, Avg Response Time.
/// D-17: Same time period selector as conversion dashboard (ResolvePeriod logic identical to REPT-02).
/// </summary>
public class GetAgentPerformanceDashboardEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/reports/agents", Handle)
        .WithSummary("Agent performance metrics: leads handled, tasks completed, conversion rate")
        .WithTags("Reports")
        .RequireAuthorization();

    public record AgentMetricsDto(
        Guid AgentId,
        string AgentName,
        int LeadsAssigned,
        int LeadsConverted,
        decimal ConversionRate,
        int TasksCompleted,
        double? AvgResponseTimeHours);  // Time from lead assignment to first activity on that lead

    public record AgentPerformanceDashboardResponse(
        string Period,
        DateTime PeriodFrom,
        DateTime PeriodTo,
        List<AgentMetricsDto> Agents);

    // Per D-17: identical period resolution to REPT-02
    private static (DateTime from, DateTime to) ResolvePeriod(string? period, DateTime? customFrom, DateTime? customTo)
    {
        var now = DateTime.UtcNow;
        return period?.ToLowerInvariant() switch
        {
            "today" => (now.Date, now.Date.AddDays(1).AddTicks(-1)),
            "thisweek" => (now.Date.AddDays(-(int)now.DayOfWeek), now.Date.AddDays(7 - (int)now.DayOfWeek).AddTicks(-1)),
            "thisquarter" => (new DateTime(now.Year, ((now.Month - 1) / 3) * 3 + 1, 1), now),
            "thisyear" => (new DateTime(now.Year, 1, 1), now),
            "custom" when customFrom.HasValue && customTo.HasValue => (customFrom.Value, customTo.Value),
            _ => (new DateTime(now.Year, now.Month, 1), now)  // Default: This Month
        };
    }

    private static async Task<Ok<AgentPerformanceDashboardResponse>> Handle(
        string? period,
        DateTime? customFrom,
        DateTime? customTo,
        string? sortBy,   // "leadsAssigned", "leadsConverted", "conversionRate", "tasksCompleted"
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var (startDate, endDate) = ResolvePeriod(period, customFrom, customTo);
        var periodLabel = period ?? "thismonth";

        // Load all users (agents) in this tenant
        var users = await db.Users
            .Select(u => new { u.Id, u.Name })
            .ToListAsync(cancellationToken);

        var agentMetrics = new List<AgentMetricsDto>();

        foreach (var user in users)
        {
            // D-15: leads assigned (handled) in period
            var assignedLeads = await db.Leads
                .Where(l => l.AssignedToUserId == user.Id
                            && l.CreatedAt >= startDate
                            && l.CreatedAt <= endDate)
                .ToListAsync(cancellationToken);

            var leadsAssigned = assignedLeads.Count;
            var leadsConverted = assignedLeads.Count(l => l.IsConverted);
            var conversionRate = leadsAssigned > 0 ? (decimal)leadsConverted / leadsAssigned : 0m;

            // D-15: tasks completed in period
            var tasksCompleted = await db.LeadTasks
                .CountAsync(t => t.AssignedToUserId == user.Id
                                 && t.Status == IronMonkey.Data.Entities.TaskStatus.Completed
                                 && t.UpdatedAt >= startDate
                                 && t.UpdatedAt <= endDate,
                    cancellationToken);

            // D-16: avg response time = avg time from lead assignment to first activity on that lead
            // Uses ActivityLog: first ActivityLog.CreatedAt for ActorId=user.Id on each assigned lead
            double? avgResponseHours = null;
            if (assignedLeads.Any())
            {
                var leadIds = assignedLeads.Select(l => l.Id).ToList();
                var firstActivities = await db.ActivityLogs
                    .Where(a => leadIds.Contains(a.LeadId) && a.ActorId == user.Id)
                    .GroupBy(a => a.LeadId)
                    .Select(g => new { LeadId = g.Key, FirstActivity = g.Min(a => a.CreatedAt) })
                    .ToListAsync(cancellationToken);

                var responseTimes = assignedLeads
                    .Join(firstActivities,
                        lead => lead.Id,
                        activity => activity.LeadId,
                        (lead, activity) => (activity.FirstActivity - lead.CreatedAt).TotalHours)
                    .Where(hours => hours >= 0)
                    .ToList();

                if (responseTimes.Any())
                    avgResponseHours = responseTimes.Average();
            }

            if (leadsAssigned > 0 || tasksCompleted > 0)  // Only include agents with activity
            {
                agentMetrics.Add(new AgentMetricsDto(
                    user.Id,
                    user.Name,
                    leadsAssigned,
                    leadsConverted,
                    conversionRate,
                    tasksCompleted,
                    avgResponseHours));
            }
        }

        // Per D-16: sortable — apply sort
        var sorted = (sortBy?.ToLowerInvariant() switch
        {
            "leadsconverted" => agentMetrics.OrderByDescending(a => a.LeadsConverted),
            "conversionrate" => agentMetrics.OrderByDescending(a => a.ConversionRate),
            "taskscompleted" => agentMetrics.OrderByDescending(a => a.TasksCompleted),
            _ => agentMetrics.OrderByDescending(a => a.LeadsAssigned)  // Default: by leads assigned
        }).ToList();

        return TypedResults.Ok(new AgentPerformanceDashboardResponse(periodLabel, startDate, endDate, sorted));
    }
}
