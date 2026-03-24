using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Leads.Pipeline.States;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.Pipeline.Kanban;

public class MoveLeadEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/leads/{leadId:guid}/move", Handle)
        .WithSummary("Move a lead to a new pipeline stage (validates configured transitions)")
        .WithTags("Pipeline")
        .RequireAuthorization();

    public record Request(Guid TargetStageId);
    public record Response(Guid LeadId, Guid NewStageId);

    private static async Task<Results<Ok<Response>, NotFound, BadRequest<string>>> Handle(
        Guid leadId,
        Request request,
        IStateValidationService stateValidation,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        // Validate transition before mutating
        var error = await stateValidation.ValidateTransitionAsync(
            tenantId, connectionString, leadId, request.TargetStageId, cancellationToken);

        if (error != null)
            return TypedResults.BadRequest(error);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);
        var lead = await db.Leads.FindAsync(new object[] { leadId }, cancellationToken);

        if (lead == null) return TypedResults.NotFound();

        lead.MoveToPipelineStage(request.TargetStageId);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(lead.Id, lead.PipelineStageId));
    }
}
