using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Configuration;
using IronMonkey.ApiService.Features.Pipelines;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Opportunities.Stages;

public class UpdateOpportunityStageEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/opportunity-stages/{id:guid}", Handle)
        .WithSummary("Update an opportunity stage name, order, type and active state")
        .WithTags("Opportunity Stages")
        .RequireAuthorization();

    /// <param name="Order">Optional — omitted or non-positive leaves the stage where it is.</param>
    /// <param name="IsActive">Optional — omitted leaves the active state unchanged.</param>
    /// <param name="StageType">Optional — omitted leaves the type unchanged.</param>
    public record Request(string Name, int Order = 0, bool? IsActive = null, string? StageType = null);

    public record Response(Guid Id, string Name, int Order, bool IsActive, string StageType);

    private static async Task<Results<Ok<Response>, NotFound, BadRequest<string>, Conflict<string>>> Handle(
        Guid id,
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IConfigurationUsageService usageService,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return TypedResults.BadRequest("Stage name must be between 1 and 100 characters.");

        StageType? newType = null;
        if (request.StageType is not null)
        {
            if (!Enum.TryParse<StageType>(request.StageType, out var parsed))
                return TypedResults.BadRequest("StageType must be one of: Entry, Active, ClosedWon, ClosedLost.");
            newType = parsed;
        }

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var stage = await OpportunityStageResolver.FindAsync(db, id, cancellationToken);
        if (stage is null)
            return TypedResults.NotFound();

        // Uniqueness is checked within the stage's OWN pipeline, matching the index. A
        // second pipeline may legitimately have a stage of the same name at the same
        // position — they are different funnels.
        var nameExists = await PipelineStageResolution.Stages(db, stage.PipelineId)
            .AnyAsync(s => s.Id != id && s.Name.ToLower() == name.ToLower(), cancellationToken);

        if (nameExists)
            return TypedResults.Conflict($"A stage named '{name}' already exists in this pipeline.");

        if (request.Order > 0 && request.Order != stage.Order)
        {
            var orderExists = await PipelineStageResolution.Stages(db, stage.PipelineId)
                .AnyAsync(s => s.Order == request.Order && s.Id != id, cancellationToken);

            if (orderExists)
                return TypedResults.Conflict($"A stage with order {request.Order} already exists in this pipeline.");

            stage.SetOrder(request.Order);
        }

        // Deactivating a stage that still holds deals hides them from every board and filter
        // without moving them anywhere. Refuse and point the Admin at the impact endpoint,
        // which offers the reassignment targets.
        if (request.IsActive is false && stage.IsActive)
        {
            var usage = await usageService.GetOpportunityStageUsageAsync(db, id, cancellationToken);

            if (usage.IsReferenced)
            {
                return TypedResults.Conflict(
                    $"'{stage.Name}' still holds {usage.LeadCount} " +
                    $"opportunit{(usage.LeadCount == 1 ? "y" : "ies")}. " +
                    "Move them to another stage before deactivating it.");
            }

            if (usage.IsOnlyActiveStage)
            {
                return TypedResults.Conflict(
                    "This is the only active stage. A pipeline needs at least one active stage to accept deals.");
            }
        }

        stage.Rename(name);
        if (request.IsActive.HasValue) stage.SetActive(request.IsActive.Value);
        if (newType.HasValue) stage.SetStageType(newType.Value);

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(
            stage.Id, stage.Name, stage.Order, stage.IsActive, stage.StageType.ToString()));
    }
}
