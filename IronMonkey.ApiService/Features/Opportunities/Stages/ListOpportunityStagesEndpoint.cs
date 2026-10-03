using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Pipelines;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Opportunities.Stages;

/// <summary>
/// The opportunity stage endpoints deliberately mirror
/// <c>Features/Leads/PipelineStages/</c> rule for rule. They operate on the same entity with
/// <c>RecordType = Opportunity</c>, so the protections are not reimplemented — only the
/// record they count and the route they sit on differ.
/// </summary>
public class ListOpportunityStagesEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/opportunity-stages", Handle)
        .WithSummary("List opportunity stages for the authenticated tenant ordered by Order")
        .WithTags("Opportunity Stages")
        .RequireAuthorization();

    public record Response(
        Guid Id, string Name, int Order, bool IsActive, string StageType, bool IsTerminal,
        Guid PipelineId, string PipelineName);

    /// <param name="includeInactive">
    /// Nullable on purpose: a non-nullable bool query parameter is *required* in minimal
    /// APIs, so every caller that omitted it — the opportunity form and list among them —
    /// would 500 rather than defaulting.
    /// </param>
    /// <param name="pipelineId">
    /// Which pipeline's stages to list. Omitted returns the default pipeline's — never every
    /// pipeline's merged together, which would offer a stage picker choices from funnels the
    /// record is not in. Pass "all" to list across every pipeline deliberately.
    /// </param>
    private static async Task<Results<Ok<List<Response>>, BadRequest<string>>> Handle(
        bool? includeInactive,
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
            db, PipelineRecordType.Opportunity, pipelineId, cancellationToken);

        if (!scope.IsValid) return TypedResults.BadRequest(scope.Error!);

        var scoped = scope.PipelineIds;

        var query = db.PipelineStages
            .Include(s => s.Pipeline)
            .Where(s => s.RecordType == PipelineRecordType.Opportunity && scoped.Contains(s.PipelineId));

        if (includeInactive != true)
            query = query.Where(s => s.IsActive);

        var stages = await query
            // Grouped by pipeline first so an "all" listing reads as several ordered funnels
            // rather than as one sequence with duplicated positions.
            .OrderBy(s => s.Pipeline.Order)
            .ThenBy(s => s.Order)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(stages
            .Select(s => new Response(
                s.Id, s.Name, s.Order, s.IsActive, s.StageType.ToString(), s.IsTerminal,
                s.PipelineId, s.Pipeline.Name))
            .ToList());
    }
}
