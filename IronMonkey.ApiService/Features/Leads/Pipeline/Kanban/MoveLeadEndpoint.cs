using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Leads.Pipeline.States;
using IronMonkey.ApiService.Features.Leads.Workflow.Rules;
using IronMonkey.ApiService.Features.Pipelines;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

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
        IWorkflowTriggerDispatcher workflowTriggers,
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

        // The target stage must belong to the lead's OWN pipeline.
        //
        // Without this a caller could post any stage id and land the lead on a stage from a
        // different pipeline — it would then show on neither board, be excluded from its own
        // pipeline's per-stage totals and counted in another's. That is the central
        // correctness risk of multiple pipelines, and a board drag is the easiest way to hit
        // it, so it is refused here rather than relied upon from the UI.
        //
        // Moving a record BETWEEN pipelines is a different operation with different
        // semantics (it picks a landing stage and records the jump as a pipeline change) and
        // lives at POST /api/leads/{id}/pipeline.
        var targetInPipeline = await PipelineStageResolution.FindInPipelineAsync(
            db, lead.PipelineId, request.TargetStageId, cancellationToken);

        if (targetInPipeline is null)
        {
            return TypedResults.BadRequest(
                "That stage does not belong to this lead's pipeline. " +
                "Use the pipeline move endpoint to put the lead in a different pipeline.");
        }

        lead.MoveToPipelineStage(request.TargetStageId);
        await db.SaveChangesAsync(cancellationToken);

        workflowTriggers.Dispatch(tenantId, lead.Id, WorkflowTrigger.StatusChange);

        return TypedResults.Ok(new Response(lead.Id, lead.PipelineStageId));
    }
}
