using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Configuration;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.PipelineStages;

/// <summary>
/// Reports what deleting or deactivating a stage would affect, so the confirmation dialog can
/// state the consequence instead of asking "are you sure?" about an unknown number of leads.
/// </summary>
public class GetPipelineStageImpactEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/pipeline-stages/{id:guid}/impact", Handle)
        .WithSummary("Report the records affected by removing or deactivating a pipeline stage")
        .WithTags("Pipeline Stages")
        .RequireAuthorization();

    /// <param name="ReassignTargets">Other active stages the leads could be moved to.</param>
    public record Response(
        Guid Id,
        string Name,
        int LeadCount,
        int TransitionCount,
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

        var stage = await db.PipelineStages.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (stage is null) return TypedResults.NotFound();

        var usage = await usageService.GetStageUsageAsync(db, id, cancellationToken);

        var targets = await db.PipelineStages
            .Where(p => p.Id != id && p.IsActive)
            .OrderBy(p => p.Order)
            .Select(p => new ReassignTarget(p.Id, p.Name))
            .ToListAsync(cancellationToken);

        // Deleting is only ever offered when nothing points at the stage and the pipeline
        // would still have somewhere to put a lead.
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
