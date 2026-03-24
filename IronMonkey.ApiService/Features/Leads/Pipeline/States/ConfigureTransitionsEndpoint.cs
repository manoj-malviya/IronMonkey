using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Pipeline.States;

public class ConfigureTransitionsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/stage-transitions", Handle)
        .WithSummary("Configure an allowed stage transition for the tenant pipeline")
        .WithTags("Pipeline")
        .RequireAuthorization();

    public record Request(Guid FromStageId, Guid ToStageId);
    public record Response(Guid Id, Guid FromStageId, Guid ToStageId);

    private static async Task<Results<Created<Response>, BadRequest<string>, Conflict<string>>> Handle(
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (request.FromStageId == request.ToStageId)
            return TypedResults.BadRequest("FromStageId and ToStageId must be different.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        // Verify both stages belong to this tenant
        var fromExists = await db.PipelineStages.AnyAsync(p => p.Id == request.FromStageId, cancellationToken);
        var toExists = await db.PipelineStages.AnyAsync(p => p.Id == request.ToStageId, cancellationToken);

        if (!fromExists || !toExists)
            return TypedResults.BadRequest("One or both stage IDs do not exist for this tenant.");

        var alreadyExists = await db.StageTransitions
            .AnyAsync(t => t.FromStageId == request.FromStageId && t.ToStageId == request.ToStageId,
                cancellationToken);

        if (alreadyExists)
            return TypedResults.Conflict("This transition is already configured.");

        var transition = StageTransition.Create(tenantId, request.FromStageId, request.ToStageId);
        db.StageTransitions.Add(transition);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/stage-transitions/{transition.Id}",
            new Response(transition.Id, transition.FromStageId, transition.ToStageId));
    }
}
