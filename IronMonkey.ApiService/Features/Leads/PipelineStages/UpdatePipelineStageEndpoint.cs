using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.PipelineStages;

public class UpdatePipelineStageEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/pipeline-stages/{id:guid}", Handle)
        .WithSummary("Update a pipeline stage name and order for the authenticated tenant")
        .WithTags("Pipeline Stages")
        .RequireAuthorization();

    public record Request(string Name, int Order);
    public record Response(Guid Id, string Name, int Order, bool IsActive);

    private static async Task<Results<Ok<Response>, NotFound, BadRequest<string>, Conflict<string>>> Handle(
        Guid id,
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
            return TypedResults.BadRequest("Stage name must be between 1 and 100 characters.");

        if (request.Order <= 0)
            return TypedResults.BadRequest("Order must be greater than 0.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var stage = await db.PipelineStages
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (stage is null)
            return TypedResults.NotFound();

        // Check order uniqueness, excluding the current stage
        var orderExists = await db.PipelineStages
            .AnyAsync(p => p.Order == request.Order && p.Id != id, cancellationToken);

        if (orderExists)
            return TypedResults.Conflict($"A pipeline stage with order {request.Order} already exists.");

        stage.Update(request.Name, request.Order);
        await db.SaveChangesAsync(cancellationToken);

        var response = new Response(stage.Id, stage.Name, stage.Order, stage.IsActive);
        return TypedResults.Ok(response);
    }
}
