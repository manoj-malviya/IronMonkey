using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Pipelines;
using IronMonkey.Data.Entities;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Reports.Dashboard;

/// <summary>
/// Headline tenant metrics for the CRM dashboard: open leads, conversion rate and the
/// leads-by-stage breakdown.
///
/// Each dashboard widget is its own endpoint rather than one combined payload, so a
/// widget that fails (or is slow) degrades alone — the page renders the rest and offers
/// a retry for just that panel.
///
/// Every count is produced by a grouped SQL aggregate. Nothing here loads entity rows
/// into memory, so the cost is independent of how many leads the tenant holds.
/// </summary>
public class GetDashboardSummaryEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/dashboard/summary", Handle)
        .WithSummary("Tenant dashboard headline metrics and leads-by-stage breakdown")
        .WithTags("Dashboard")
        .RequireAuthorization();

    public record StageBreakdownItem(
        Guid StageId,
        string StageName,
        int Order,
        string StageType,
        bool IsTerminal,
        int LeadCount);

    public record DashboardSummaryResponse(
        DateTime RangeFrom,
        DateTime RangeTo,
        string Preset,
        int TotalLeads,
        int OpenLeads,
        int ConvertedLeads,
        decimal ConversionRate,
        int TotalContacts,
        bool HasAnyStages,
        List<StageBreakdownItem> ByStage,
        /// <summary>Which pipeline(s) every figure above covers — a name, or "All pipelines".</summary>
        string ScopeLabel,
        Guid? PipelineId,
        bool IsTenantWide,
        bool IsMultiPipeline,
        DateTime GeneratedAt);

    /// <param name="pipelineId">
    /// Which lead pipeline the figures cover. Omitted means the default one, NOT all of
    /// them — a widening default is exactly how a tenant ends up reading a cross-pipeline
    /// sum under one pipeline's heading. Pass "all" for the tenant-wide figure.
    /// </param>
    internal static async Task<Results<Ok<DashboardSummaryResponse>, BadRequest<string>>> Handle(
        string? preset,
        DateTime? from,
        DateTime? to,
        string? pipelineId,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IPipelineScopeResolver scopeResolver,
        CancellationToken cancellationToken)
    {
        var range = DashboardDateRange.Resolve(preset, from, to);

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var scope = await scopeResolver.ResolveAsync(
            db, PipelineRecordType.Lead, pipelineId, cancellationToken);

        if (!scope.IsValid) return TypedResults.BadRequest(scope.Error!);

        var scopedPipelines = scope.PipelineIds;

        // The global query filters on TenantDbContext already scope every set below to this
        // tenant and exclude soft-deleted rows, so no TenantId predicate is repeated here.
        // The pipeline predicate is NOT covered by those filters and is stated explicitly on
        // every set that feeds a figure.
        var leadsInRange = db.Leads
            .Where(l => l.CreatedAt >= range.From && l.CreatedAt < range.ToExclusive)
            .Where(l => scopedPipelines.Contains(l.PipelineId));

        var stages = await db.PipelineStages
            .Where(s => s.IsActive
                        && s.RecordType == PipelineRecordType.Lead
                        && scopedPipelines.Contains(s.PipelineId))
            .OrderBy(s => s.Order)
            .Select(s => new { s.Id, s.Name, s.Order, s.StageType })
            .ToListAsync(cancellationToken);

        // One grouped round-trip for every stage's count, then joined in memory against the
        // stage list — a per-stage CountAsync would be N queries for an N-stage pipeline.
        var countsByStage = await leadsInRange
            .GroupBy(l => l.PipelineStageId)
            .Select(g => new { StageId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StageId, x => x.Count, cancellationToken);

        var byStage = stages
            .Select(s => new StageBreakdownItem(
                s.Id,
                s.Name,
                s.Order,
                s.StageType.ToString(),
                s.StageType is Data.Entities.StageType.ClosedWon or Data.Entities.StageType.ClosedLost,
                countsByStage.TryGetValue(s.Id, out var n) ? n : 0))
            .ToList();

        var totalLeads = await leadsInRange.CountAsync(cancellationToken);
        var convertedLeads = await leadsInRange.CountAsync(l => l.IsConverted, cancellationToken);

        // "Open" excludes both converted leads and anything parked in a terminal stage: a
        // lead sitting in Closed Lost is not open work even though it was never converted.
        var terminalStageIds = stages
            .Where(s => s.StageType is Data.Entities.StageType.ClosedWon or Data.Entities.StageType.ClosedLost)
            .Select(s => s.Id)
            .ToList();

        var openLeads = await leadsInRange
            .CountAsync(l => !l.IsConverted && !terminalStageIds.Contains(l.PipelineStageId), cancellationToken);

        var totalContacts = await db.Contacts
            .CountAsync(c => c.CreatedAt >= range.From && c.CreatedAt < range.ToExclusive, cancellationToken);

        var conversionRate = totalLeads > 0 ? (decimal)convertedLeads / totalLeads : 0m;

        return TypedResults.Ok(new DashboardSummaryResponse(
            range.From,
            range.ToInclusive,
            range.Preset,
            totalLeads,
            openLeads,
            convertedLeads,
            conversionRate,
            totalContacts,
            // Lets the UI tell "this tenant has no pipeline configured yet" (offer setup)
            // apart from "configured but no leads in this range" (offer create / widen range).
            stages.Count > 0,
            byStage,
            scope.ScopeLabel,
            scope.SelectedPipelineId,
            scope.IsTenantWide,
            scope.IsMultiPipelineTenant,
            DateTime.UtcNow));
    }
}
