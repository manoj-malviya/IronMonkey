using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Pipelines;

/// <summary>
/// Reports everything removing a pipeline would affect, before anything is removed.
///
/// A pipeline is the largest configuration object a tenant owns — it transitively holds
/// stages, records, fields, rules and routing — so "are you sure?" is a useless question
/// about it. This endpoint answers the useful one: exactly what would move and what would
/// change scope.
/// </summary>
public class GetPipelineImpactEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/pipelines/{id:guid}/impact", Handle)
        .WithSummary("Report what removing a pipeline would affect")
        .WithTags("Pipelines")
        .RequireAuthorization();

    /// <param name="ScopedRuleCount">
    /// Workflow rules that target this pipeline. They are NOT deleted with it — they revert
    /// to tenant-wide, which the response says explicitly so the Admin is not surprised by a
    /// rule that suddenly applies more broadly than it did.
    /// </param>
    /// <param name="IsDefault">
    /// The default pipeline is never removable. Removing it would leave the record type with
    /// no answer for "no pipeline specified", which every existing caller depends on.
    /// Promote another pipeline first.
    /// </param>
    public record Response(
        Guid Id,
        string Name,
        string RecordType,
        int StageCount,
        int RecordCount,
        int ScopedFieldCount,
        int ScopedRuleCount,
        int ScopedRoutingCount,
        int HistoryCount,
        bool IsDefault,
        bool IsOnlyPipeline,
        bool CanRemove,
        string? Blocker,
        List<ReassignTarget> ReassignTargets);

    /// <param name="EntryStageId">
    /// The stage a record reassigned into this pipeline would land on. Named explicitly so
    /// the confirmation can state where records are going — a move that does not say which
    /// stage it lands in is the silent cross-pipeline placement this whole feature guards
    /// against.
    /// </param>
    public record ReassignTarget(Guid Id, string Name, Guid? EntryStageId, string? EntryStageName);

    internal static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var pipeline = await db.Pipelines.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pipeline is null) return TypedResults.NotFound();

        var stageCount = await db.PipelineStages.CountAsync(s => s.PipelineId == id, cancellationToken);

        var recordCount = pipeline.RecordType == PipelineRecordType.Lead
            ? await db.Leads.CountAsync(l => l.PipelineId == id, cancellationToken)
            : await db.Opportunities.CountAsync(o => o.PipelineId == id, cancellationToken);

        var scopedFields = await db.CustomFieldDefinitions.CountAsync(f => f.PipelineId == id, cancellationToken);
        var scopedRules = await db.WorkflowRules.CountAsync(r => r.PipelineId == id, cancellationToken);
        var scopedRouting = await db.RoutingConfigs.CountAsync(r => r.PipelineId == id, cancellationToken);
        var historyCount = await db.StageChanges.CountAsync(c => c.ToPipelineId == id, cancellationToken);

        var siblingCount = await db.Pipelines
            .CountAsync(p => p.RecordType == pipeline.RecordType, cancellationToken);

        var isOnly = siblingCount <= 1;

        // Every other active pipeline of the same record type, each with the stage a
        // reassigned record would land on.
        var others = await db.Pipelines
            .Where(p => p.RecordType == pipeline.RecordType && p.Id != id && p.IsActive)
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Order)
            .ToListAsync(cancellationToken);

        var targets = new List<ReassignTarget>();
        foreach (var other in others)
        {
            var entry = await PipelineStageResolution.GetEntryStageAsync(db, other.Id, cancellationToken);
            targets.Add(new ReassignTarget(other.Id, other.Name, entry?.Id, entry?.Name));
        }

        string? blocker = null;
        if (pipeline.IsDefault)
            blocker = "This is the default pipeline. Make another pipeline the default before removing it.";
        else if (isOnly)
            blocker = "This is the only pipeline for this record type. A record type needs at least one.";
        else if (recordCount > 0 && targets.Count == 0)
            blocker = "There is no other active pipeline to move these records to.";

        return TypedResults.Ok(new Response(
            pipeline.Id,
            pipeline.Name,
            pipeline.RecordType.ToString(),
            stageCount,
            recordCount,
            scopedFields,
            scopedRules,
            scopedRouting,
            historyCount,
            pipeline.IsDefault,
            isOnly,
            CanRemove: blocker is null,
            blocker,
            targets));
    }
}
