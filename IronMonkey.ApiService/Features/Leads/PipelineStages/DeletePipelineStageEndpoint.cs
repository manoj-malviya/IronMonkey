using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.PipelineStages;

public class DeletePipelineStageEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapDelete("/api/pipeline-stages/{id:guid}", Handle)
        .WithSummary("Deactivate a pipeline stage for the authenticated tenant")
        .WithTags("Pipeline Stages")
        .RequireAuthorization();

    public record Response(bool Success, string? Message);

    private static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var stage = await db.PipelineStages
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (stage is null) return TypedResults.NotFound();

        stage.Deactivate();
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(true, null));
    }
}
