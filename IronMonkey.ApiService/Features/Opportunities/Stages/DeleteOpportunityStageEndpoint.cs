using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Configuration;
using IronMonkey.ApiService.Features.Pipelines;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Opportunities.Stages;

public class DeleteOpportunityStageEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapDelete("/api/opportunity-stages/{id:guid}", Handle)
        .WithSummary("Deactivate an opportunity stage, optionally reassigning its deals first")
        .WithTags("Opportunity Stages")
        .RequireAuthorization();

    public record Response(bool Success, string? Message, int ReassignedOpportunities);

    /// <param name="reassignTo">
    /// Stage to move this stage's opportunities to. Required when the stage still holds any —
    /// without it the request is refused rather than stranding them.
    /// </param>
    private static async Task<Results<Ok<Response>, NotFound, Conflict<string>, BadRequest<string>>> Handle(
        Guid id,
        Guid? reassignTo,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IConfigurationUsageService usageService,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var stage = await OpportunityStageResolver.FindAsync(db, id, cancellationToken);
        if (stage is null) return TypedResults.NotFound();

        var usage = await usageService.GetOpportunityStageUsageAsync(db, id, cancellationToken);

        if (usage.IsOnlyActiveStage)
        {
            return TypedResults.Conflict(
                "This is the only active stage. A pipeline needs at least one active stage to accept deals.");
        }

        var reassigned = 0;

        if (usage.IsReferenced)
        {
            if (reassignTo is null)
            {
                return TypedResults.Conflict(
                    $"'{stage.Name}' still holds {usage.LeadCount} " +
                    $"opportunit{(usage.LeadCount == 1 ? "y" : "ies")}. " +
                    "Choose a stage to move them to.");
            }

            if (reassignTo == id)
                return TypedResults.BadRequest("Opportunities cannot be reassigned to the stage being removed.");

            // Resolved within the SAME PIPELINE as the stage being removed. The record-type
            // filter alone stopped a LEAD stage being used as a target; this also stops
            // another opportunity PIPELINE's stage, which would strand every reassigned deal
            // on a stage its own pipeline does not contain.
            var target = await PipelineStageResolution.Stages(db, stage.PipelineId)
                .SingleOrDefaultAsync(s => s.Id == reassignTo && s.IsActive, cancellationToken);

            if (target is null)
            {
                return TypedResults.BadRequest(
                    "The stage chosen for reassignment does not exist in this pipeline, or is not active.");
            }
        }

        // The move and the deactivation are one decision, so they commit together — a failure
        // between them would leave deals in a stage no board shows any more.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (usage.IsReferenced)
        {
            var opportunities = await db.Opportunities
                .Where(o => o.PipelineStageId == id)
                .ToListAsync(cancellationToken);

            foreach (var opportunity in opportunities)
            {
                // A forced reassignment is a real stage change and is recorded as one:
                // otherwise a deal's history shows it in a stage it silently left.
                StageChangeRecorder.Record(
                    db, tenantId, PipelineRecordType.Opportunity, opportunity.Id,
                    opportunity.PipelineStageId, reassignTo!.Value, userContext.UserId,
                    // Reassignment is within one pipeline — the target stage was resolved
                    // from the same pipeline as the stage being removed — so the pipeline is
                    // unchanged on both sides.
                    fromPipelineId: opportunity.PipelineId, toPipelineId: opportunity.PipelineId);

                opportunity.MoveToPipelineStage(reassignTo!.Value);
            }

            reassigned = opportunities.Count;
        }

        stage.Deactivate();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var message = reassigned > 0
            ? $"Moved {reassigned} opportunit{(reassigned == 1 ? "y" : "ies")} and deactivated '{stage.Name}'."
            : null;

        return TypedResults.Ok(new Response(true, message, reassigned));
    }
}
