using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Configuration;
using IronMonkey.ApiService.Features.Pipelines;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Opportunities.Stages;

/// <summary>
/// Reports what deleting or deactivating an opportunity stage would affect, so the
/// confirmation dialog can state the consequence instead of asking "are you sure?" about an
/// unknown number of deals.
/// </summary>
public class GetOpportunityStageImpactEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/opportunity-stages/{id:guid}/impact", Handle)
        .WithSummary("Report the records affected by removing or deactivating an opportunity stage")
        .WithTags("Opportunity Stages")
        .RequireAuthorization();

    /// <param name="ReassignTargets">Other active opportunity stages the deals could be moved to.</param>
    public record Response(
        Guid Id,
        string Name,
        int OpportunityCount,
        int HistoryCount,
        bool IsOnlyActiveStage,
        bool CanDelete,
        List<ReassignTarget> ReassignTargets);

    public record ReassignTarget(Guid Id, string Name);

    private static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IConfigurationUsageService usageService,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var stage = await OpportunityStageResolver.FindAsync(db, id, cancellationToken);
        if (stage is null) return TypedResults.NotFound();

        var usage = await usageService.GetOpportunityStageUsageAsync(db, id, cancellationToken);

        // From the stage's OWN pipeline only — a target in another pipeline would strand
        // every reassigned deal on a stage its pipeline does not contain.
        var targets = await PipelineStageResolution.Stages(db, stage.PipelineId)
            .Where(s => s.Id != id && s.IsActive)
            .OrderBy(s => s.Order)
            .Select(s => new ReassignTarget(s.Id, s.Name))
            .ToListAsync(cancellationToken);

        // Removing is only ever offered when nothing points at the stage and the pipeline
        // would still have somewhere to put a deal.
        var canDelete = !usage.IsReferenced && usage.TransitionCount == 0 && !usage.IsOnlyActiveStage;

        return TypedResults.Ok(new Response(
            stage.Id,
            stage.Name,
            usage.LeadCount,
            usage.TransitionCount,
            usage.IsOnlyActiveStage,
            canDelete,
            targets));
    }
}
