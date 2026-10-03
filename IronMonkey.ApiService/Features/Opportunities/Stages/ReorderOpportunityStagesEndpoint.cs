using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Pipelines;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Opportunities.Stages;

/// <summary>
/// Rewrites the whole opportunity stage order in one transaction.
///
/// Reordering one stage at a time through the update endpoint is not safe: every intermediate
/// state is a real, visible ordering, and a failure halfway leaves the pipeline scrambled. The
/// client sends the complete sequence it wants and gets it applied atomically or not at all.
/// </summary>
public class ReorderOpportunityStagesEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/opportunity-stages/order", Handle)
        .WithSummary("Atomically set the order of every opportunity stage for the authenticated tenant")
        .WithTags("Opportunity Stages")
        .RequireAuthorization();

    /// <param name="StageIds">Every opportunity stage id, in the order they should appear.</param>
    /// <param name="PipelineId">
    /// The pipeline whose sequence is being rewritten. Omitted means the tenant's default
    /// opportunity pipeline. Each pipeline owns an independent 1..n sequence, so a reorder
    /// is always scoped to one — submitting every pipeline's stages at once would interleave
    /// two funnels into one ordering space.
    /// </param>
    public record Request(List<Guid> StageIds, Guid? PipelineId = null);

    public record Response(Guid Id, string Name, int Order, bool IsActive, string StageType);

    private static async Task<Results<Ok<List<Response>>, BadRequest<string>, Conflict<string>>> Handle(
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (request.StageIds is null || request.StageIds.Count == 0)
            return TypedResults.BadRequest("At least one stage id is required.");

        if (request.StageIds.Distinct().Count() != request.StageIds.Count)
            return TypedResults.BadRequest("The same stage appears more than once in the order.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var pipeline = await PipelineTarget.ResolveAsync(
            db, PipelineRecordType.Opportunity, request.PipelineId, cancellationToken);

        if (pipeline is null)
        {
            return TypedResults.BadRequest(
                "The requested pipeline does not exist for opportunities in this tenant.");
        }

        // Only THIS PIPELINE's stages. Comparing against every opportunity stage in the
        // tenant would make the set check below fail for any tenant with a second pipeline —
        // and would let one pipeline's reorder silently renumber another's.
        var stages = await PipelineStageResolution.Stages(db, pipeline.Id)
            .ToListAsync(cancellationToken);

        // A partial list would silently leave the omitted stages at positions that now
        // collide with the new sequence. Require the caller to have seen the same set it is
        // reordering — this is also what catches a concurrent create from another admin,
        // and a stage id from a DIFFERENT PIPELINE smuggled into the list — it is simply not
        // in this pipeline's stored set, so the set comparison rejects it.
        var submitted = request.StageIds.ToHashSet();
        var existing = stages.Select(s => s.Id).ToHashSet();

        if (!submitted.SetEquals(existing))
        {
            return TypedResults.Conflict(
                "The stage list has changed since it was loaded. Reload the configuration and reorder again.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var byId = stages.ToDictionary(s => s.Id);
        for (var i = 0; i < request.StageIds.Count; i++)
            byId[request.StageIds[i]].SetOrder(i + 1);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var response = stages
            .OrderBy(s => s.Order)
            .Select(s => new Response(s.Id, s.Name, s.Order, s.IsActive, s.StageType.ToString()))
            .ToList();

        return TypedResults.Ok(response);
    }
}
