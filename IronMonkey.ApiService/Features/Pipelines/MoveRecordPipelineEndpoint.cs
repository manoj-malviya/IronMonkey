using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Configuration;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Pipelines;

/// <summary>
/// Moves one lead or one opportunity into a different pipeline.
///
/// <para><b>What a move does, stated explicitly:</b></para>
/// <list type="number">
/// <item><b>Stage.</b> The record lands on a stage <i>of the destination pipeline</i> — the
///   one named in <c>StageId</c> if it belongs there, otherwise that pipeline's entry stage.
///   The stage it held before is never carried over: it belongs to the old pipeline, and a
///   record pointing at another pipeline's stage is the silent-corruption case this whole
///   feature has to prevent. A named stage that is not in the destination is a 400, never a
///   quiet substitution.</item>
/// <item><b>History.</b> One <c>stage_changes</c> row is written carrying both the old and
///   the new pipeline, so the jump is legible as a pipeline change rather than as an
///   inexplicable stage move. Prior history is left untouched — the record really was in the
///   old pipeline, and rewriting that would corrupt every velocity report already computed.</item>
/// <item><b>Routing.</b> Existing assignment is <i>kept</i>. Re-running routing would
///   reassign a record whose owner has been working it, possibly to someone in a different
///   team, as a side effect of an administrative reorganisation. A move is not a new lead.
///   Future routing decisions for the record use the destination pipeline's config, because
///   routing resolves by the record's current pipeline at the time it runs.</item>
/// </list>
/// </summary>
public class MoveRecordPipelineEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/leads/{id:guid}/pipeline", HandleLead)
            .WithSummary("Move a lead into a different pipeline")
            .WithTags("Leads")
            .RequireAuthorization();

        app.MapPost("/api/opportunities/{id:guid}/pipeline", HandleOpportunity)
            .WithSummary("Move an opportunity into a different pipeline")
            .WithTags("Opportunities")
            .RequireAuthorization();
    }

    /// <param name="StageId">
    /// Optional. A stage of the destination pipeline to land on. Omitted lands on the
    /// destination's entry stage.
    /// </param>
    public record Request(Guid PipelineId, Guid? StageId = null);

    public record Response(
        Guid RecordId,
        Guid PipelineId,
        string PipelineName,
        Guid StageId,
        string StageName,
        string Message);

    internal static Task<Results<Ok<Response>, NotFound, BadRequest<string>, Conflict<string>>> HandleLead(
        Guid id, Request request, ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory, IUserContext userContext, CancellationToken ct) =>
        MoveAsync(id, request, PipelineRecordType.Lead, tenantService, dbContextFactory, userContext, ct);

    internal static Task<Results<Ok<Response>, NotFound, BadRequest<string>, Conflict<string>>> HandleOpportunity(
        Guid id, Request request, ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory, IUserContext userContext, CancellationToken ct) =>
        MoveAsync(id, request, PipelineRecordType.Opportunity, tenantService, dbContextFactory, userContext, ct);

    private static async Task<Results<Ok<Response>, NotFound, BadRequest<string>, Conflict<string>>> MoveAsync(
        Guid id,
        Request request,
        PipelineRecordType recordType,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        // Destination must be this tenant's, of this record type, and usable. A lead pipeline
        // is not a destination for a deal, and the record type predicate is what says so.
        var destination = await db.Pipelines.SingleOrDefaultAsync(
            p => p.Id == request.PipelineId && p.RecordType == recordType, cancellationToken);

        if (destination is null)
            return TypedResults.BadRequest("The destination pipeline does not exist for this record type.");

        if (!destination.IsActive)
            return TypedResults.Conflict($"'{destination.Name}' is inactive and cannot accept records.");

        Lead? lead = null;
        Opportunity? opportunity = null;
        Guid currentPipelineId;
        Guid currentStageId;

        if (recordType == PipelineRecordType.Lead)
        {
            lead = await db.Leads.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
            if (lead is null) return TypedResults.NotFound();
            currentPipelineId = lead.PipelineId;
            currentStageId = lead.PipelineStageId;
        }
        else
        {
            opportunity = await db.Opportunities.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
            if (opportunity is null) return TypedResults.NotFound();
            currentPipelineId = opportunity.PipelineId;
            currentStageId = opportunity.PipelineStageId;
        }

        if (currentPipelineId == destination.Id)
        {
            return TypedResults.Conflict(
                $"This record is already in '{destination.Name}'. " +
                "Use the stage endpoint to move it between stages within a pipeline.");
        }

        // Resolve the landing stage inside the DESTINATION. The two branches are kept
        // separate so a named-but-foreign stage is a clear 400 rather than being silently
        // replaced by the entry stage — a caller that named a stage deserves to be told its
        // stage was not where it thought, not to have the system pick a different one.
        PipelineStage? landingStage;

        if (request.StageId is { } requestedStageId)
        {
            landingStage = await PipelineStageResolution.FindInPipelineAsync(
                db, destination.Id, requestedStageId, cancellationToken);

            if (landingStage is null)
            {
                return TypedResults.BadRequest(
                    $"That stage does not belong to '{destination.Name}'. " +
                    "Choose a stage from the destination pipeline, or omit it to use its entry stage.");
            }

            if (!landingStage.IsActive)
                return TypedResults.BadRequest($"Stage '{landingStage.Name}' is inactive.");
        }
        else
        {
            landingStage = await PipelineStageResolution.GetEntryStageAsync(
                db, destination.Id, cancellationToken);

            if (landingStage is null)
            {
                return TypedResults.Conflict(
                    $"'{destination.Name}' has no active non-terminal stage for the record to land in.");
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        StageChangeRecorder.Record(
            db, tenantId, recordType, id, currentStageId, landingStage.Id, userContext.UserId,
            fromPipelineId: currentPipelineId, toPipelineId: destination.Id);

        // One call sets both ids. Assignment is deliberately untouched — see the class
        // remarks on routing.
        lead?.MoveToPipeline(destination.Id, landingStage.Id);
        opportunity?.MoveToPipeline(destination.Id, landingStage.Id);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(new Response(
            id,
            destination.Id,
            destination.Name,
            landingStage.Id,
            landingStage.Name,
            $"Moved to '{destination.Name}' at stage '{landingStage.Name}'. Assignment and history were kept."));
    }
}
