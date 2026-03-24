using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Reports.Conversion;

/// <summary>
/// REPT-02: Conversion rate dashboard — breakdown by stage, lead source, and time period.
/// D-12: Conversion rate = leads reaching ClosedWon / total leads entering pipeline.
/// D-13: Preset periods (Today/ThisWeek/ThisMonth/ThisQuarter/ThisYear) + custom date range.
/// D-14: Breakdown dimensions — by stage (funnel), by lead source, by time period.
/// </summary>
public class GetConversionDashboardEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/reports/conversion", Handle)
        .WithSummary("Conversion rates by stage, lead source, and time period")
        .WithTags("Reports")
        .RequireAuthorization();

    public record StageConversionDto(
        Guid StageId, string StageName, int TotalLeads, int ConvertedLeads, decimal ConversionRate);

    public record SourceConversionDto(
        string Source, int TotalLeads, int ConvertedLeads, decimal ConversionRate);

    public record ConversionDashboardResponse(
        string Period,
        DateTime PeriodFrom,
        DateTime PeriodTo,
        int TotalLeads,
        int TotalConverted,
        decimal OverallConversionRate,
        List<StageConversionDto> ByStage,
        List<SourceConversionDto> BySource);

    // Per D-13: preset period shortcuts
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
            _ => (new DateTime(now.Year, now.Month, 1), now)  // Default: This Month (D-13)
        };
    }

    private static async Task<Ok<ConversionDashboardResponse>> Handle(
        string? period,          // "today", "thisweek", "thismonth", "thisquarter", "thisyear", "custom"
        DateTime? customFrom,
        DateTime? customTo,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var (startDate, endDate) = ResolvePeriod(period, customFrom, customTo);
        var periodLabel = period ?? "thismonth";

        // Base query: leads created in the selected period
        var leadsInPeriod = db.Leads
            .Where(l => l.CreatedAt >= startDate && l.CreatedAt <= endDate);

        var totalLeads = await leadsInPeriod.CountAsync(cancellationToken);
        var totalConverted = await leadsInPeriod.CountAsync(l => l.IsConverted, cancellationToken);
        var overallRate = totalLeads > 0 ? (decimal)totalConverted / totalLeads : 0m;

        // By stage: funnel view — D-14
        var stages = await db.PipelineStages
            .Where(p => p.IsActive)
            .OrderBy(p => p.Order)
            .ToListAsync(cancellationToken);

        var byStage = new List<StageConversionDto>();
        foreach (var stage in stages)
        {
            var stageLeads = await leadsInPeriod.CountAsync(l => l.PipelineStageId == stage.Id, cancellationToken);
            var stageConverted = await leadsInPeriod.CountAsync(l => l.PipelineStageId == stage.Id && l.IsConverted, cancellationToken);
            byStage.Add(new StageConversionDto(
                stage.Id, stage.Name, stageLeads, stageConverted,
                stageLeads > 0 ? (decimal)stageConverted / stageLeads : 0m));
        }

        // By lead source — D-14
        var bySource = await leadsInPeriod
            .GroupBy(l => l.Source)
            .Select(g => new
            {
                Source = g.Key,
                Total = g.Count(),
                Converted = g.Count(l => l.IsConverted)
            })
            .ToListAsync(cancellationToken);

        var bySourceDto = bySource.Select(s => new SourceConversionDto(
            s.Source.ToString(),
            s.Total,
            s.Converted,
            s.Total > 0 ? (decimal)s.Converted / s.Total : 0m)).ToList();

        return TypedResults.Ok(new ConversionDashboardResponse(
            periodLabel, startDate, endDate,
            totalLeads, totalConverted, overallRate,
            byStage, bySourceDto));
    }
}
