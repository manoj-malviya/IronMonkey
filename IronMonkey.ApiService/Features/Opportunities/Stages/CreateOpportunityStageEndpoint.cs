using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Pipelines;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Opportunities.Stages;

public class CreateOpportunityStageEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/opportunity-stages", Handle)
        .WithSummary("Create a new opportunity stage for the authenticated tenant")
        .WithTags("Opportunity Stages")
        .RequireAuthorization();

    /// <param name="Order">
    /// Optional. Omitted or non-positive appends the stage to the end, as the lead stage
    /// endpoint does — the configuration UI does not ask an Admin to pick a number.
    /// </param>
    /// <param name="StageType">Entry, Active, ClosedWon or ClosedLost. Defaults to Active.</param>
    /// <param name="PipelineId">
    /// The pipeline to create the stage in. Omitted uses the tenant's default opportunity
    /// pipeline, which for a single-pipeline tenant is the only one there is — so the form
    /// never has to ask.
    /// </param>
    public record Request(string Name, int Order = 0, string StageType = "Active", Guid? PipelineId = null);

    public record Response(Guid Id, string Name, int Order, bool IsActive, string StageType, Guid PipelineId);

    private static async Task<Results<Created<Response>, BadRequest<string>, Conflict<string>>> Handle(
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return TypedResults.BadRequest("Stage name must be between 1 and 100 characters.");

        if (!Enum.TryParse<StageType>(request.StageType, out var stageType))
            return TypedResults.BadRequest("StageType must be one of: Entry, Active, ClosedWon, ClosedLost.");

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

        // Case-insensitive and scoped to THIS PIPELINE — the unique index is on
        // (TenantId, PipelineId, lower(Name)), so two pipelines may each have a "Qualified"
        // stage. Checking here turns a 500 into a usable message.
        var nameExists = await PipelineStageResolution.Stages(db, pipeline.Id)
            .AnyAsync(s => s.Name.ToLower() == name.ToLower(), cancellationToken);

        if (nameExists)
            return TypedResults.Conflict(
                $"An opportunity stage named '{name}' already exists in '{pipeline.Name}'.");

        int order;
        if (request.Order > 0)
        {
            // Order is unique per pipeline, not per tenant: each pipeline owns an
            // independent 1..n sequence, so the same position in two pipelines is normal.
            var orderExists = await PipelineStageResolution.Stages(db, pipeline.Id)
                .AnyAsync(s => s.Order == request.Order, cancellationToken);

            if (orderExists)
                return TypedResults.Conflict(
                    $"A stage with order {request.Order} already exists in '{pipeline.Name}'.");

            order = request.Order;
        }
        else
        {
            var maxOrder = await PipelineStageResolution.Stages(db, pipeline.Id)
                .Select(s => (int?)s.Order)
                .MaxAsync(cancellationToken) ?? 0;

            order = maxOrder + 1;
        }

        var stage = PipelineStage.CreateIn(pipeline, name, order, stageType);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/opportunity-stages/{stage.Id}",
            new Response(stage.Id, stage.Name, stage.Order, stage.IsActive,
                stage.StageType.ToString(), stage.PipelineId));
    }
}
