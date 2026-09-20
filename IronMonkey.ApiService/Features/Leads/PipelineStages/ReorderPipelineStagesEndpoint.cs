using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.PipelineStages;

/// <summary>
/// Rewrites the whole stage order in one transaction.
///
/// Reordering one stage at a time through the update endpoint is not safe: every
/// intermediate state is a real, visible ordering, and a failure halfway leaves the pipeline
/// scrambled. The client sends the complete sequence it wants and gets it applied atomically
/// or not at all.
/// </summary>
public class ReorderPipelineStagesEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/pipeline-stages/order", Handle)
        .WithSummary("Atomically set the order of every pipeline stage for the authenticated tenant")
        .WithTags("Pipeline Stages")
        .RequireAuthorization();

    /// <param name="StageIds">Every stage id, in the order they should appear.</param>
    public record Request(List<Guid> StageIds);

    public record Response(Guid Id, string Name, int Order, bool IsActive);

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

        var stages = await db.PipelineStages.ToListAsync(cancellationToken);

        // A partial list would silently leave the omitted stages at positions that now
        // collide with the new sequence. Require the caller to have seen the same set it is
        // reordering — this is also what catches a concurrent create from another admin.
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
            .Select(s => new Response(s.Id, s.Name, s.Order, s.IsActive))
            .ToList();

        return TypedResults.Ok(response);
    }
}
