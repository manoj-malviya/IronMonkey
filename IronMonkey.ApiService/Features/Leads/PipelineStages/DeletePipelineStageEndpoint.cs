using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Configuration;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.PipelineStages;

public class DeletePipelineStageEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapDelete("/api/pipeline-stages/{id:guid}", Handle)
        .WithSummary("Deactivate a pipeline stage, optionally reassigning its leads first")
        .WithTags("Pipeline Stages")
        .RequireAuthorization();

    public record Response(bool Success, string? Message, int ReassignedLeads);

    /// <param name="reassignTo">
    /// Stage to move this stage's leads to. Required when the stage still holds leads —
    /// without it the request is refused rather than stranding them.
    /// </param>
    private static async Task<Results<Ok<Response>, NotFound, Conflict<string>, BadRequest<string>>> Handle(
        Guid id,
        Guid? reassignTo,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IConfigurationUsageService usageService,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var stage = await db.PipelineStages
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (stage is null) return TypedResults.NotFound();

        var usage = await usageService.GetStageUsageAsync(db, id, cancellationToken);

        if (usage.IsOnlyActiveStage)
        {
            return TypedResults.Conflict(
                "This is the only active stage. A pipeline needs at least one active stage to accept leads.");
        }

        var reassigned = 0;

        if (usage.IsReferenced)
        {
            if (reassignTo is null)
            {
                return TypedResults.Conflict(
                    $"'{stage.Name}' still holds {usage.LeadCount} lead{(usage.LeadCount == 1 ? "" : "s")}. " +
                    "Choose a stage to move them to.");
            }

            if (reassignTo == id)
                return TypedResults.BadRequest("Leads cannot be reassigned to the stage being removed.");

            var target = await db.PipelineStages
                .SingleOrDefaultAsync(p => p.Id == reassignTo && p.IsActive, cancellationToken);

            if (target is null)
                return TypedResults.BadRequest("The stage chosen for reassignment does not exist or is not active.");
        }

        // The move and the deactivation are one decision, so they commit together — a failure
        // between them would leave leads in a stage the board no longer shows.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (usage.IsReferenced)
        {
            var leads = await db.Leads
                .Where(l => l.PipelineStageId == id)
                .ToListAsync(cancellationToken);

            foreach (var lead in leads)
                lead.MoveToPipelineStage(reassignTo!.Value);

            reassigned = leads.Count;
        }

        stage.Deactivate();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var message = reassigned > 0
            ? $"Moved {reassigned} lead{(reassigned == 1 ? "" : "s")} and deactivated '{stage.Name}'."
            : null;

        return TypedResults.Ok(new Response(true, message, reassigned));
    }
}
