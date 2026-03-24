using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Reports.Pipeline;

/// <summary>
/// REPT-01: Pipeline overview dashboard — leads per stage with total deal value.
/// Uses direct LINQ aggregation against tenant DB (no materialized views, per D-07).
/// Indexes on PipelineStageId and CreatedAt ensure fast execution (D-08).
/// </summary>
public class GetPipelineDashboardEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/reports/pipeline", Handle)
        .WithSummary("Pipeline overview: lead count and total deal value per stage")
        .WithTags("Reports")
        .RequireAuthorization();

    public record StageMetricsDto(
        Guid StageId,
        string StageName,
        int Order,
        string StageType,      // "Entry", "Active", "ClosedWon", "ClosedLost"
        bool IsTerminal,       // Per D-11: ClosedWon/ClosedLost visually distinct
        int LeadCount,
        decimal TotalDealValue,
        int ConvertedCount,
        decimal ConversionRate);

    public record PipelineDashboardResponse(
        List<StageMetricsDto> Stages,
        int TotalLeads,
        decimal TotalDealValue,
        DateTime GeneratedAt);

    private static async Task<Ok<PipelineDashboardResponse>> Handle(
        DateTime? dateFrom,
        DateTime? dateTo,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        // Load active stages ordered by pipeline position
        var stages = await db.PipelineStages
            .Where(p => p.IsActive)
            .OrderBy(p => p.Order)
            .ToListAsync(cancellationToken);

        var stageMetrics = new List<StageMetricsDto>();

        foreach (var stage in stages)
        {
            // Filter leads by stage and optional date range
            var leadsQuery = db.Leads.Where(l => l.PipelineStageId == stage.Id);
            if (dateFrom.HasValue) leadsQuery = leadsQuery.Where(l => l.CreatedAt >= dateFrom.Value);
            if (dateTo.HasValue) leadsQuery = leadsQuery.Where(l => l.CreatedAt <= dateTo.Value);

            var leadCount = await leadsQuery.CountAsync(cancellationToken);
            var convertedCount = await leadsQuery.CountAsync(l => l.IsConverted, cancellationToken);

            // Per D-10: total deal value = sum of Opportunity.Amount for converted leads in this stage
            // Leads without a converted opportunity contribute $0
            var dealValue = await db.Leads
                .Where(l => l.PipelineStageId == stage.Id
                            && l.ConvertedOpportunityId != null
                            && (!dateFrom.HasValue || l.CreatedAt >= dateFrom.Value)
                            && (!dateTo.HasValue || l.CreatedAt <= dateTo.Value))
                .Join(db.Opportunities,
                    lead => lead.ConvertedOpportunityId,
                    opp => opp.Id,
                    (lead, opp) => opp.Amount)
                .SumAsync(amount => (decimal?)amount, cancellationToken) ?? 0m;

            var conversionRate = leadCount > 0 ? (decimal)convertedCount / leadCount : 0m;

            stageMetrics.Add(new StageMetricsDto(
                stage.Id,
                stage.Name,
                stage.Order,
                stage.StageType.ToString(),
                stage.IsTerminal,
                leadCount,
                dealValue,
                convertedCount,
                conversionRate));
        }

        return TypedResults.Ok(new PipelineDashboardResponse(
            stageMetrics,
            stageMetrics.Sum(s => s.LeadCount),
            stageMetrics.Sum(s => s.TotalDealValue),
            DateTime.UtcNow));
    }
}
