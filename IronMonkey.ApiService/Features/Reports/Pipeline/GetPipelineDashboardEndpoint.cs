using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Pipelines;
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
        decimal ConversionRate,
        Guid PipelineId,
        string PipelineName);

    /// <param name="ByPipeline">
    /// Per-pipeline subtotals. Derived from the same stage rows as the totals, so the parts
    /// always sum to the whole — and so a multi-pipeline tenant can see the split instead of
    /// one aggregate number whose composition is invisible.
    /// </param>
    public record PipelineBreakdownItem(
        Guid PipelineId, string PipelineName, int LeadCount, decimal TotalDealValue);

    public record PipelineDashboardResponse(
        List<StageMetricsDto> Stages,
        int TotalLeads,
        decimal TotalDealValue,
        List<PipelineBreakdownItem> ByPipeline,
        string ScopeLabel,
        Guid? PipelineId,
        bool IsTenantWide,
        bool IsMultiPipeline,
        DateTime GeneratedAt);

    /// <param name="pipelineId">
    /// Which lead pipeline to report on. Omitted means the default one, not all of them.
    /// Pass "all" for the tenant-wide figure, which the response labels and breaks down.
    /// </param>
    internal static async Task<Results<Ok<PipelineDashboardResponse>, BadRequest<string>>> Handle(
        DateTime? dateFrom,
        DateTime? dateTo,
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

        // Active stages of the pipelines in scope, ordered by position within each.
        //
        // The RecordType predicate also fixes a pre-existing defect: opportunity stages live
        // in this same table since Part A, so without it this LEAD dashboard listed deal
        // stages as empty rows. The pipeline filter alone would already exclude them (an
        // opportunity stage belongs to an opportunity pipeline), but it is stated explicitly
        // because that is the invariant being relied on.
        var stages = await db.PipelineStages
            .Include(p => p.Pipeline)
            .Where(p => p.IsActive
                        && p.RecordType == PipelineRecordType.Lead
                        && scopedPipelines.Contains(p.PipelineId))
            .OrderBy(p => p.Pipeline.Order)
            .ThenBy(p => p.Order)
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
                conversionRate,
                stage.PipelineId,
                stage.Pipeline.Name));
        }

        var byPipeline = stageMetrics
            .GroupBy(s => new { s.PipelineId, s.PipelineName })
            .OrderBy(g => g.Key.PipelineName, StringComparer.Ordinal)
            .Select(g => new PipelineBreakdownItem(
                g.Key.PipelineId, g.Key.PipelineName,
                g.Sum(s => s.LeadCount), g.Sum(s => s.TotalDealValue)))
            .ToList();

        return TypedResults.Ok(new PipelineDashboardResponse(
            stageMetrics,
            stageMetrics.Sum(s => s.LeadCount),
            stageMetrics.Sum(s => s.TotalDealValue),
            byPipeline,
            scope.ScopeLabel,
            scope.SelectedPipelineId,
            scope.IsTenantWide,
            scope.IsMultiPipelineTenant,
            DateTime.UtcNow));
    }
}
