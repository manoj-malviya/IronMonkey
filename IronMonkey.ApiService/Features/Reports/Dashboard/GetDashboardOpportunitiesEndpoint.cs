using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Commerce;
using IronMonkey.ApiService.Features.Pipelines;
using IronMonkey.Data;
using IronMonkey.Data.Commerce;
using IronMonkey.Data.Entities;

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

    /// <param name="StageType">
    /// The stage's configured type. Won/lost is read from here and never from the name — a
    /// tenant that renames "Won" to "Closed - Order Placed" must still see its own won
    /// figures, and a tenant with an active stage called "Won" must not have it counted as
    /// closed business.
    /// </param>
    public record OpportunityStageItem(
        Guid StageId, string Stage, string StageType, int Count, decimal TotalValue, bool IsTerminal,
        Guid PipelineId, string PipelineName);

    /// <summary>
    /// Per-pipeline subtotals, present whenever the scope covers more than one pipeline.
    ///
    /// This is the direct answer to "a tenant with two pipelines must never see one
    /// pipeline's counts presented as the tenant total". When the figures span pipelines the
    /// widget can show the split rather than a single number whose composition is invisible,
    /// and when they do not, this holds exactly one entry which the UI can omit.
    /// </summary>
    public record PipelineBreakdownItem(
        Guid PipelineId, string PipelineName, int Count, decimal TotalValue,
        int OpenCount, decimal OpenValue, int WonCount, decimal WonValue);

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
        List<PipelineBreakdownItem> ByPipeline,
        /// <summary>Which pipeline(s) every figure above covers — a name, or "All pipelines".</summary>
        string ScopeLabel,
        Guid? PipelineId,
        bool IsTenantWide,
        bool IsMultiPipeline,
        DateTime GeneratedAt,
        /// <summary>The currency every value above is in (null = tenant has configured none).</summary>
        string? CurrencyCode = null,
        /// <summary>
        /// Deals in another currency with no recorded exchange rate. They are counted in the
        /// counts but NOT in any value — never added at face value — and the UI says so.
        /// </summary>
        int UnconvertedCount = 0,
        Dictionary<string, decimal>? UnconvertedAmounts = null);

    /// <param name="pipelineId">
    /// Which pipeline the figures cover. Omitted means the default pipeline, NOT every
    /// pipeline — an omitted parameter that widened the scope is exactly how one pipeline's
    /// heading ends up over the tenant total. Pass "all" for the tenant-wide figure, which
    /// the response then labels as such and breaks down per pipeline.
    /// </param>
    internal static async Task<Results<Ok<DashboardOpportunitiesResponse>, BadRequest<string>>> Handle(
        string? preset,
        DateTime? from,
        DateTime? to,
        string? pipelineId,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IPipelineScopeResolver scopeResolver,
        CancellationToken cancellationToken,
        ITenantCommerceContextResolver? commerce = null)
    {
        var range = DashboardDateRange.Resolve(preset, from, to);

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var scope = await scopeResolver.ResolveAsync(
            db, PipelineRecordType.Opportunity, pipelineId, cancellationToken);

        if (!scope.IsValid) return TypedResults.BadRequest(scope.Error!);

        var scopedPipelines = scope.PipelineIds;

        // Group by the stage id and carry the stage's own name, order and type out of the
        // join, so a renamed stage reports under its current name with no mapping table.
        //
        // The pipeline predicate sits beside the date range and is equally non-optional:
        // every figure this endpoint returns is the sum over exactly the pipelines the
        // caller asked for.
        var grouped = await db.Opportunities
            .Where(o => o.CreatedAt >= range.From && o.CreatedAt < range.ToExclusive)
            .Where(o => scopedPipelines.Contains(o.PipelineId))
            // Currency and recorded rate are part of the key: a SQL SUM over Amount alone
            // would add pounds to euros. Each currency subgroup is converted (or excluded)
            // in memory below by MoneyAggregator, the one place that rule lives.
            .GroupBy(o => new
            {
                o.PipelineStageId, o.Stage.Name, o.Stage.Order, o.Stage.StageType,
                o.PipelineId, PipelineName = o.Pipeline.Name,
                o.CurrencyCode, o.ExchangeRate
            })
            .Select(g => new
            {
                g.Key.PipelineStageId,
                g.Key.Name,
                g.Key.Order,
                g.Key.StageType,
                g.Key.PipelineId,
                g.Key.PipelineName,
                g.Key.CurrencyCode,
                g.Key.ExchangeRate,
                Count = g.Count(),
                // Sum over an empty group cannot happen here (a group exists only because a
                // row matched), but the nullable cast keeps the translation total either way.
                Value = g.Sum(o => (decimal?)o.Amount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var baseCurrency = commerce is null ? null : (await commerce.ResolveAsync(tenantId, cancellationToken)).BaseCurrency;
        var overall = MoneyAggregator.Sum(grouped.Select(g => new MoneyValue(g.Value, g.CurrencyCode, g.ExchangeRate)), baseCurrency);

        // Counted in deals, not in currency subgroups.
        var unconvertedDeals = grouped
            .Where(g => !MoneyMath.SameCurrency(g.CurrencyCode, null, baseCurrency) && g.ExchangeRate is not > 0)
            .Sum(g => g.Count);

        var grouped2 = grouped
            .GroupBy(g => new { g.PipelineStageId, g.Name, g.Order, g.StageType, g.PipelineId, g.PipelineName })
            .Select(g => new
            {
                g.Key.PipelineStageId,
                g.Key.Name,
                g.Key.Order,
                g.Key.StageType,
                g.Key.PipelineId,
                g.Key.PipelineName,
                Count = g.Sum(x => x.Count),
                TotalValue = MoneyAggregator.Sum(g.Select(x => new MoneyValue(x.Value, x.CurrencyCode, x.ExchangeRate)), baseCurrency).Total
            })
            .ToList();

        // Ordered by the tenant's own configured stage order, so the widget reads as that
        // tenant's funnel rather than as a hardcoded one.
        var byStage = grouped2
            // Pipeline leads the sort: across several pipelines the stage orders repeat, so
            // sorting by order alone would interleave two funnels into one unreadable list.
            .OrderBy(g => g.PipelineName, StringComparer.Ordinal)
            .ThenBy(g => g.Order)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .Select(g => new OpportunityStageItem(
                g.PipelineStageId,
                g.Name,
                g.StageType.ToString(),
                g.Count,
                g.TotalValue,
                g.StageType is StageType.ClosedWon or StageType.ClosedLost,
                g.PipelineId,
                g.PipelineName))
            .ToList();

        var won = byStage.Where(s => s.StageType == nameof(StageType.ClosedWon)).ToList();
        var open = byStage.Where(s => !s.IsTerminal).ToList();

        // Subtotals per pipeline, derived from the same rows as the totals so the parts can
        // never fail to sum to the whole.
        var byPipeline = byStage
            .GroupBy(s => new { s.PipelineId, s.PipelineName })
            .OrderBy(g => g.Key.PipelineName, StringComparer.Ordinal)
            .Select(g => new PipelineBreakdownItem(
                g.Key.PipelineId,
                g.Key.PipelineName,
                g.Sum(s => s.Count),
                g.Sum(s => s.TotalValue),
                g.Where(s => !s.IsTerminal).Sum(s => s.Count),
                g.Where(s => !s.IsTerminal).Sum(s => s.TotalValue),
                g.Where(s => s.StageType == nameof(StageType.ClosedWon)).Sum(s => s.Count),
                g.Where(s => s.StageType == nameof(StageType.ClosedWon)).Sum(s => s.TotalValue)))
            .ToList();

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
            byPipeline,
            scope.ScopeLabel,
            scope.SelectedPipelineId,
            scope.IsTenantWide,
            scope.IsMultiPipelineTenant,
            DateTime.UtcNow,
            baseCurrency,
            unconvertedDeals,
            overall.ExcludedAmounts.ToDictionary(kv => kv.Key, kv => kv.Value)));
    }
}
