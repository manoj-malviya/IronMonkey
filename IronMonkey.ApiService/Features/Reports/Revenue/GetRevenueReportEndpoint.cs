using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Commerce;
using IronMonkey.ApiService.Features.Pipelines;
using IronMonkey.ApiService.Features.Reports.Dashboard;
using IronMonkey.Data;
using IronMonkey.Data.Commerce;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Reports.Revenue;

/// <summary>
/// Revenue from line items: by product, by category, recurring versus one-off, discount
/// analysis and win rate per product.
///
/// <para><b>Population.</b> Deals whose <c>ExpectedCloseDate</c> falls in the half-open
/// range — the deal's close date, which is what "revenue in Q3" means. Won revenue counts
/// deals in a <see cref="StageType.ClosedWon"/> stage; win rate is won ÷ (won + lost) among
/// closed deals containing the product. Stage type, never stage name, decides both.</para>
///
/// <para><b>Currency.</b> Every figure is in the tenant base currency. A line on a deal in
/// another currency is converted through the rate recorded on that deal, or — with no rate
/// recorded — excluded and listed in <c>Unconverted</c>. Nothing is ever summed at face value
/// across currencies.</para>
///
/// <para>Aggregation runs over line rows in memory rather than in SQL because the per-line
/// conversion is the shared <see cref="MoneyAggregator"/> rule; reproducing it in SQL would
/// be a second implementation of the rule that could drift from the first.</para>
/// </summary>
public class GetRevenueReportEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/reports/revenue", Handle)
        .WithSummary("Revenue by product and category, recurring vs one-off, discounts and win rates")
        .WithTags("Reports")
        .RequireAuthorization();

    /// <summary>Label used for free-text lines, which have no product to group under.</summary>
    public const string UnlistedLabel = "Unlisted items";
    public const string UncategorisedLabel = "Uncategorised";

    public sealed record ProductRevenueItem(
        Guid? ProductId, string Name, string? Code, string Category,
        decimal Quantity, int DealCount, decimal Revenue, decimal OneOffRevenue, decimal RecurringRevenue,
        decimal ListValue, decimal Discount, decimal DiscountRatePercent,
        int WonDeals, int LostDeals, decimal? WinRatePercent);

    public sealed record CategoryRevenueItem(string Category, decimal Revenue, decimal OneOffRevenue,
        decimal RecurringRevenue, decimal Discount, int DealCount);

    public sealed record RecurrenceItem(string BillingFrequency, decimal Revenue, int LineCount);

    public sealed record RevenueReportResponse(
        DateTime RangeFrom, DateTime RangeTo, string Preset, string ScopeLabel,
        string? CurrencyCode,
        int WonDealCount, decimal WonRevenue, decimal WonOneOffRevenue, decimal WonRecurringRevenue,
        int OpenDealCount, decimal OpenPipelineValue, decimal OpenRecurringValue,
        decimal WonListValue, decimal WonDiscount, decimal WonDiscountRatePercent,
        List<ProductRevenueItem> ByProduct,
        List<CategoryRevenueItem> ByCategory,
        List<RecurrenceItem> RecurringByFrequency,
        int UnconvertedDealCount,
        Dictionary<string, decimal> Unconverted,
        DateTime GeneratedAt);

    private sealed record LineRow(
        Guid OpportunityId, StageType StageType, string? CurrencyCode, decimal? Rate,
        Guid? ProductId, string Description, string? Code, string? Category,
        ChargeType ChargeType, BillingFrequency Frequency, decimal Quantity,
        decimal Gross, decimal Discount, decimal Total);

    internal static async Task<Results<Ok<RevenueReportResponse>, BadRequest<string>>> Handle(
        string? preset,
        DateTime? from,
        DateTime? to,
        string? pipelineId,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IPipelineScopeResolver scopeResolver,
        ITenantCommerceContextResolver commerce,
        CancellationToken cancellationToken)
    {
        var range = ForCloseDates(DashboardDateRange.Resolve(preset, from, to));

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var scope = await scopeResolver.ResolveAsync(db, PipelineRecordType.Opportunity, pipelineId, cancellationToken);
        if (!scope.IsValid) return TypedResults.BadRequest(scope.Error!);
        var pipelines = scope.PipelineIds;

        var baseCurrency = (await commerce.ResolveAsync(tenantId, cancellationToken)).BaseCurrency;

        var rows = await db.OpportunityLineItems
            .Join(db.Opportunities, l => l.OpportunityId, o => o.Id, (l, o) => new { l, o })
            .Where(x => x.o.ExpectedCloseDate >= range.From && x.o.ExpectedCloseDate < range.ToExclusive)
            .Where(x => pipelines.Contains(x.o.PipelineId))
            .Select(x => new LineRow(
                x.o.Id, x.o.Stage.StageType, x.o.CurrencyCode, x.o.ExchangeRate,
                x.l.ProductId, x.l.Description, x.l.ProductCode, x.l.Category,
                x.l.ChargeType, x.l.BillingFrequency, x.l.Quantity,
                x.l.GrossAmount, x.l.DiscountAmount, x.l.TotalAmount))
            .ToListAsync(cancellationToken);

        var unconvertedRows = rows.Where(r => !Convertible(r, baseCurrency)).ToList();
        var counted = rows.Where(r => Convertible(r, baseCurrency)).ToList();

        decimal Sum(IEnumerable<LineRow> source, Func<LineRow, decimal> pick) =>
            MoneyAggregator.Sum(source.Select(r => new MoneyValue(pick(r), r.CurrencyCode, r.Rate)), baseCurrency).Total;

        var won = counted.Where(r => r.StageType == StageType.ClosedWon).ToList();
        var open = counted.Where(r => r.StageType is not (StageType.ClosedWon or StageType.ClosedLost)).ToList();
        var closed = counted.Where(r => r.StageType is StageType.ClosedWon or StageType.ClosedLost).ToList();

        // Product rows: revenue from won deals; win rate from all closed deals containing it.
        var byProduct = closed
            .GroupBy(r => r.ProductId)
            .Select(g =>
            {
                var wonLines = g.Where(r => r.StageType == StageType.ClosedWon).ToList();
                var wonDeals = wonLines.Select(r => r.OpportunityId).Distinct().Count();
                var lostDeals = g.Where(r => r.StageType == StageType.ClosedLost).Select(r => r.OpportunityId).Distinct().Count();
                var first = g.First();
                var gross = Sum(wonLines, r => r.Gross);
                var discount = Sum(wonLines, r => r.Discount);

                return new ProductRevenueItem(
                    g.Key,
                    g.Key is null ? UnlistedLabel : first.Description,
                    g.Key is null ? null : first.Code,
                    first.Category ?? UncategorisedLabel,
                    wonLines.Sum(r => r.Quantity),
                    wonDeals,
                    Sum(wonLines, r => r.Total),
                    Sum(wonLines.Where(r => r.ChargeType == ChargeType.OneOff), r => r.Total),
                    Sum(wonLines.Where(r => r.ChargeType == ChargeType.Recurring), r => r.Total),
                    gross, discount, Rate(discount, gross),
                    wonDeals, lostDeals,
                    wonDeals + lostDeals == 0 ? null : Math.Round(100m * wonDeals / (wonDeals + lostDeals), 1));
            })
            .OrderByDescending(p => p.Revenue).ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        var byCategory = won
            .GroupBy(r => r.Category ?? UncategorisedLabel)
            .Select(g => new CategoryRevenueItem(
                g.Key,
                Sum(g, r => r.Total),
                Sum(g.Where(r => r.ChargeType == ChargeType.OneOff), r => r.Total),
                Sum(g.Where(r => r.ChargeType == ChargeType.Recurring), r => r.Total),
                Sum(g, r => r.Discount),
                g.Select(r => r.OpportunityId).Distinct().Count()))
            .OrderByDescending(c => c.Revenue).ThenBy(c => c.Category, StringComparer.Ordinal)
            .ToList();

        var recurring = won
            .Where(r => r.ChargeType == ChargeType.Recurring)
            .GroupBy(r => r.Frequency)
            .Select(g => new RecurrenceItem(g.Key.ToString(), Sum(g, r => r.Total), g.Count()))
            .OrderByDescending(r => r.Revenue)
            .ToList();

        var wonGross = Sum(won, r => r.Gross);
        var wonDiscount = Sum(won, r => r.Discount);

        var unconverted = new Dictionary<string, decimal>();
        foreach (var r in unconvertedRows)
            unconverted[r.CurrencyCode ?? "(unspecified)"] = unconverted.GetValueOrDefault(r.CurrencyCode ?? "(unspecified)") + r.Total;

        return TypedResults.Ok(new RevenueReportResponse(
            range.From, range.ToInclusive, range.Preset, scope.ScopeLabel, baseCurrency,
            won.Select(r => r.OpportunityId).Distinct().Count(),
            Sum(won, r => r.Total),
            Sum(won.Where(r => r.ChargeType == ChargeType.OneOff), r => r.Total),
            Sum(won.Where(r => r.ChargeType == ChargeType.Recurring), r => r.Total),
            open.Select(r => r.OpportunityId).Distinct().Count(),
            Sum(open, r => r.Total),
            Sum(open.Where(r => r.ChargeType == ChargeType.Recurring), r => r.Total),
            wonGross, wonDiscount, Rate(wonDiscount, wonGross),
            byProduct, byCategory, recurring,
            unconvertedRows.Select(r => r.OpportunityId).Distinct().Count(),
            unconverted,
            DateTime.UtcNow));
    }

    /// <summary>
    /// The dashboard presets end at today because they filter creation dates, which cannot be
    /// in the future. A close date can: "this quarter's revenue" includes deals expected to
    /// close later this quarter. So a period preset is extended to the period's end, and
    /// all-time is unbounded. Rolling presets (last 7/30 days) and custom ranges are unchanged.
    /// </summary>
    internal static DashboardDateRange ForCloseDates(DashboardDateRange range)
    {
        var from = range.From;
        var end = range.Preset switch
        {
            DashboardDateRange.ThisMonth => from.AddMonths(1),
            DashboardDateRange.ThisQuarter => from.AddMonths(3),
            DashboardDateRange.ThisYear => from.AddYears(1),
            DashboardDateRange.AllTime => DateTime.SpecifyKind(DateTime.MaxValue.Date, DateTimeKind.Utc),
            _ => range.ToExclusive
        };
        return range with { ToExclusive = end };
    }

    private static bool Convertible(LineRow row, string? baseCurrency) =>
        MoneyMath.SameCurrency(row.CurrencyCode, null, baseCurrency) || row.Rate is > 0;

    private static decimal Rate(decimal part, decimal whole) => whole == 0 ? 0 : Math.Round(100m * part / whole, 1);
}
