using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Common;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Reports.Dashboard;

/// <summary>
/// Opportunity pipeline for the dashboard: count and total value per stage, plus the
/// open/won split.
///
/// Aggregation happens in SQL (one GROUP BY); the browser never receives opportunity rows.
/// </summary>
public class GetDashboardOpportunitiesEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/dashboard/opportunities", Handle)
        .WithSummary("Opportunity count and value per stage for the tenant dashboard")
        .WithTags("Dashboard")
        .RequireAuthorization();

    public record OpportunityStageItem(string Stage, int Count, decimal TotalValue, bool IsTerminal);

    public record DashboardOpportunitiesResponse(
        DateTime RangeFrom,
        DateTime RangeTo,
        string Preset,
        int TotalCount,
        decimal TotalValue,
        int OpenCount,
        decimal OpenValue,
        int WonCount,
        decimal WonValue,
        List<OpportunityStageItem> ByStage,
        DateTime GeneratedAt);

    internal static async Task<Ok<DashboardOpportunitiesResponse>> Handle(
        string? preset,
        DateTime? from,
        DateTime? to,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var range = DashboardDateRange.Resolve(preset, from, to);

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var grouped = await db.Opportunities
            .Where(o => o.CreatedAt >= range.From && o.CreatedAt < range.ToExclusive)
            .GroupBy(o => o.Stage)
            .Select(g => new
            {
                Stage = g.Key,
                Count = g.Count(),
                // Sum over an empty group cannot happen here (a group exists only because a
                // row matched), but the nullable cast keeps the translation total either way.
                TotalValue = g.Sum(o => (decimal?)o.Amount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        // Ordered by the canonical pipeline order so the widget reads as a funnel; any
        // stage string not in the canonical list (legacy or hand-written data) sorts last
        // rather than being dropped.
        var byStage = grouped
            .Select(g => new OpportunityStageItem(
                g.Stage,
                g.Count,
                g.TotalValue,
                OpportunityStages.IsTerminal(g.Stage)))
            .OrderBy(s =>
            {
                var index = OpportunityStages.All.ToList().IndexOf(s.Stage);
                return index < 0 ? int.MaxValue : index;
            })
            .ThenBy(s => s.Stage)
            .ToList();

        var won = byStage.Where(s => s.Stage == OpportunityStages.Won).ToList();
        var open = byStage.Where(s => !s.IsTerminal).ToList();

        return TypedResults.Ok(new DashboardOpportunitiesResponse(
            range.From,
            range.ToInclusive,
            range.Preset,
            byStage.Sum(s => s.Count),
            byStage.Sum(s => s.TotalValue),
            open.Sum(s => s.Count),
            open.Sum(s => s.TotalValue),
            won.Sum(s => s.Count),
            won.Sum(s => s.TotalValue),
            byStage,
            DateTime.UtcNow));
    }
}
