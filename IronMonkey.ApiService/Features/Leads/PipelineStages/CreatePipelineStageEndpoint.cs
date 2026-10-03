using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Pipelines;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.PipelineStages;

public class CreatePipelineStageEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/pipeline-stages", Handle)
        .WithSummary("Create a new pipeline stage for the authenticated tenant")
        .WithTags("Pipeline Stages")
        .RequireAuthorization();

    /// <param name="Order">
    /// Optional. Omitted or non-positive appends the stage to the end, which is what the
    /// configuration UI does — it no longer asks the Admin to pick a number.
    /// </param>
    /// <param name="PipelineId">
    /// The pipeline to create the stage in. Omitted uses the tenant's default lead pipeline.
    /// </param>
    public record Request(string Name, int Order = 0, Guid? PipelineId = null);

    public record Response(Guid Id, string Name, int Order, bool IsActive, Guid PipelineId);

    private static async Task<Results<Created<Response>, BadRequest<string>, Conflict<string>>> Handle(
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return TypedResults.BadRequest("Stage name must be between 1 and 100 characters.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var pipeline = await PipelineTarget.ResolveAsync(
            db, PipelineRecordType.Lead, request.PipelineId, cancellationToken);

        if (pipeline is null)
            return TypedResults.BadRequest("The requested pipeline does not exist for leads in this tenant.");

        // Case-insensitive and scoped to THIS PIPELINE: "Qualified" and "qualified" are the
        // same stage to a user, and the database index agrees — but two pipelines may each
        // have their own "Qualified", because they are different funnels.
        var nameExists = await PipelineStageResolution.Stages(db, pipeline.Id)
            .AnyAsync(p => p.Name.ToLower() == name.ToLower(), cancellationToken);

        if (nameExists)
            return TypedResults.Conflict($"A stage named '{name}' already exists in '{pipeline.Name}'.");

        int order;
        if (request.Order > 0)
        {
            var orderExists = await PipelineStageResolution.Stages(db, pipeline.Id)
                .AnyAsync(p => p.Order == request.Order, cancellationToken);

            if (orderExists)
                return TypedResults.Conflict($"A stage with order {request.Order} already exists in '{pipeline.Name}'.");

            order = request.Order;
        }
        else
        {
            var maxOrder = await PipelineStageResolution.Stages(db, pipeline.Id)
                .Select(p => (int?)p.Order)
                .MaxAsync(cancellationToken) ?? 0;

            order = maxOrder + 1;
        }

        var stage = PipelineStage.CreateIn(pipeline, name, order);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync(cancellationToken);

        var response = new Response(stage.Id, stage.Name, stage.Order, stage.IsActive, stage.PipelineId);
        return TypedResults.Created($"/api/pipeline-stages/{stage.Id}", response);
    }
}
