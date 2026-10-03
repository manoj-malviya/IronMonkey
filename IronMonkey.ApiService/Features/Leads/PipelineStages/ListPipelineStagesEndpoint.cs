using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Pipelines;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.PipelineStages;

public class ListPipelineStagesEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/pipeline-stages", Handle)
        .WithSummary("List all active pipeline stages for the authenticated tenant ordered by Order")
        .WithTags("Pipeline Stages")
        .RequireAuthorization();

    public record Response(
        Guid Id, string Name, int Order, bool IsActive, Guid PipelineId, string PipelineName);

    /// <param name="pipelineId">
    /// Which pipeline's stages to list. Omitted returns the tenant's default lead pipeline —
    /// for a single-pipeline tenant, the only one, so no caller has to start sending this.
    /// Pass "all" to list across every lead pipeline deliberately.
    /// </param>
    internal static async Task<Results<Ok<List<Response>>, BadRequest<string>>> Handle(
        string? pipelineId,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IPipelineScopeResolver scopeResolver,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var scope = await scopeResolver.ResolveAsync(
            db, PipelineRecordType.Lead, pipelineId, cancellationToken);

        if (!scope.IsValid) return TypedResults.BadRequest(scope.Error!);

        var scoped = scope.PipelineIds;

        // The RecordType predicate also closes a pre-existing hole: opportunity stages have
        // shared this table since Part A, so an unfiltered list handed the lead stage picker
        // deal stages. The pipeline filter would already exclude them, but the invariant is
        // stated rather than relied on implicitly.
        var stages = await db.PipelineStages
            .Include(p => p.Pipeline)
            .Where(p => p.RecordType == PipelineRecordType.Lead && scoped.Contains(p.PipelineId))
            .OrderBy(p => p.Pipeline.Order)
            .ThenBy(p => p.Order)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        var response = stages
            .Select(s => new Response(
                s.Id, s.Name, s.Order, s.IsActive, s.PipelineId, s.Pipeline.Name))
            .ToList();

        return TypedResults.Ok(response);
    }
}
