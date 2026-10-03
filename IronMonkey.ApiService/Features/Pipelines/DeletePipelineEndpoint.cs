using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Configuration;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Pipelines;

/// <summary>
/// Removes a pipeline, moving its records to another pipeline first and reverting everything
/// that targeted it to tenant-wide.
///
/// <para><b>Nothing referenced is removed silently.</b> Records must be reassigned to a named
/// pipeline, and they land on that pipeline's entry stage — never on the stage they held in
/// the pipeline being removed, which is not a stage of where they are going.</para>
///
/// <para><b>Scoped configuration reverts, it does not vanish.</b> Fields, workflow rules and
/// routing configs that targeted this pipeline have their target set to null, which makes
/// them tenant-wide again. Deleting them would destroy configuration the Admin never asked to
/// lose; leaving them pointing at a removed pipeline would make them match nothing, which is
/// deletion without saying so. Widening is the one outcome that is both reversible and
/// visible, and the response states exactly how many were widened.</para>
///
/// <para><b>History is kept.</b> <c>stage_changes</c> rows naming this pipeline are left
/// alone — what pipeline a record was in last March is a fact that removing configuration
/// today must not rewrite. This is why those columns carry no foreign key.</para>
/// </summary>
public class DeletePipelineEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapDelete("/api/pipelines/{id:guid}", Handle)
        .WithSummary("Remove a pipeline, reassigning its records and widening its scoped configuration")
        .WithTags("Pipelines")
        .RequireAuthorization();

    public record Response(
        bool Success,
        string Message,
        int ReassignedRecords,
        Guid? ReassignedToPipelineId,
        string? ReassignedToStageName,
        int WidenedFields,
        int WidenedRules,
        int WidenedRoutingConfigs);

    /// <param name="reassignTo">
    /// The pipeline this pipeline's records move to. Required whenever it holds any.
    /// </param>
    internal static async Task<Results<Ok<Response>, NotFound, Conflict<string>, BadRequest<string>>> Handle(
        Guid id,
        Guid? reassignTo,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var pipeline = await db.Pipelines.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pipeline is null) return TypedResults.NotFound();

        if (pipeline.IsDefault)
        {
            return TypedResults.Conflict(
                "This is the default pipeline. Make another pipeline the default before removing it.");
        }

        var siblingCount = await db.Pipelines
            .CountAsync(p => p.RecordType == pipeline.RecordType, cancellationToken);

        if (siblingCount <= 1)
        {
            return TypedResults.Conflict(
                "This is the only pipeline for this record type. A record type needs at least one.");
        }

        var recordCount = pipeline.RecordType == PipelineRecordType.Lead
            ? await db.Leads.CountAsync(l => l.PipelineId == id, cancellationToken)
            : await db.Opportunities.CountAsync(o => o.PipelineId == id, cancellationToken);

        Pipeline? target = null;
        PipelineStage? landingStage = null;

        if (recordCount > 0)
        {
            if (reassignTo is null)
            {
                return TypedResults.Conflict(
                    $"'{pipeline.Name}' still holds {recordCount} record(s). " +
                    "Choose a pipeline to move them to.");
            }

            if (reassignTo == id)
                return TypedResults.BadRequest("Records cannot be reassigned to the pipeline being removed.");

            // Matched on the SAME record type. A lead pipeline is not a legal destination for
            // deals, and the record type filter is the only thing that says so.
            target = await db.Pipelines.SingleOrDefaultAsync(
                p => p.Id == reassignTo && p.RecordType == pipeline.RecordType && p.IsActive,
                cancellationToken);

            if (target is null)
            {
                return TypedResults.BadRequest(
                    "The pipeline chosen for reassignment does not exist, is inactive, or is for a different record type.");
            }

            landingStage = await PipelineStageResolution.GetEntryStageAsync(db, target.Id, cancellationToken);

            if (landingStage is null)
            {
                return TypedResults.Conflict(
                    $"'{target.Name}' has no active non-terminal stage for these records to land in.");
            }
        }

        // Every effect commits together. A partial run would leave records in a pipeline
        // whose stages had already been removed, which no board would render.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var reassigned = 0;

        if (recordCount > 0 && target is not null && landingStage is not null)
        {
            if (pipeline.RecordType == PipelineRecordType.Lead)
            {
                var leads = await db.Leads.Where(l => l.PipelineId == id).ToListAsync(cancellationToken);

                foreach (var lead in leads)
                {
                    // Recorded as a real pipeline change, with both the old and new pipeline,
                    // so the record's history explains why its stage jumped.
                    StageChangeRecorder.Record(
                        db, tenantId, PipelineRecordType.Lead, lead.Id,
                        lead.PipelineStageId, landingStage.Id, userContext.UserId,
                        fromPipelineId: lead.PipelineId, toPipelineId: target.Id);

                    lead.MoveToPipeline(target.Id, landingStage.Id);
                }

                reassigned = leads.Count;
            }
            else
            {
                var opportunities = await db.Opportunities
                    .Where(o => o.PipelineId == id).ToListAsync(cancellationToken);

                foreach (var opportunity in opportunities)
                {
                    StageChangeRecorder.Record(
                        db, tenantId, PipelineRecordType.Opportunity, opportunity.Id,
                        opportunity.PipelineStageId, landingStage.Id, userContext.UserId,
                        fromPipelineId: opportunity.PipelineId, toPipelineId: target.Id);

                    opportunity.MoveToPipeline(target.Id, landingStage.Id);
                }

                reassigned = opportunities.Count;
            }
        }

        // Scoped configuration reverts to tenant-wide rather than being deleted.
        var fields = await db.CustomFieldDefinitions.Where(f => f.PipelineId == id).ToListAsync(cancellationToken);
        foreach (var field in fields) field.SetPipeline(null);

        var rules = await db.WorkflowRules.Where(r => r.PipelineId == id).ToListAsync(cancellationToken);
        foreach (var rule in rules) rule.SetPipeline(null);

        var routingConfigs = await db.RoutingConfigs.Where(r => r.PipelineId == id).ToListAsync(cancellationToken);
        foreach (var config in routingConfigs) config.SetPipeline(null);

        // Stages go with the pipeline — they are its parts, and no record points at them any
        // more. Soft-deleted, like every other configuration row, so history that references
        // them by id still resolves.
        var stages = await db.PipelineStages.Where(s => s.PipelineId == id).ToListAsync(cancellationToken);
        db.PipelineStages.RemoveRange(stages);

        db.Pipelines.Remove(pipeline);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var parts = new List<string> { $"Removed pipeline '{pipeline.Name}'." };

        if (reassigned > 0)
            parts.Add($"Moved {reassigned} record(s) to '{target!.Name}' at stage '{landingStage!.Name}'.");

        var widened = fields.Count + rules.Count + routingConfigs.Count;
        if (widened > 0)
        {
            parts.Add(
                $"{widened} item(s) that targeted this pipeline now apply to the whole tenant " +
                $"({fields.Count} field(s), {rules.Count} rule(s), {routingConfigs.Count} routing config(s)).");
        }

        return TypedResults.Ok(new Response(
            true,
            string.Join(" ", parts),
            reassigned,
            target?.Id,
            landingStage?.Name,
            fields.Count,
            rules.Count,
            routingConfigs.Count));
    }
}
