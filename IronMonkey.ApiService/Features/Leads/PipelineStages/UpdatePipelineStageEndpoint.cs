using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Configuration;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.PipelineStages;

public class UpdatePipelineStageEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/pipeline-stages/{id:guid}", Handle)
        .WithSummary("Update a pipeline stage name, order and active state for the authenticated tenant")
        .WithTags("Pipeline Stages")
        .RequireAuthorization();

    /// <param name="Order">Optional — omitted or non-positive leaves the stage where it is.</param>
    /// <param name="IsActive">Optional — omitted leaves the active state unchanged.</param>
    public record Request(string Name, int Order = 0, bool? IsActive = null);

    public record Response(Guid Id, string Name, int Order, bool IsActive);

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

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var stage = await db.PipelineStages
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (stage is null)
            return TypedResults.NotFound();

        var nameExists = await db.PipelineStages
            .AnyAsync(p => p.Id != id && p.Name.ToLower() == name.ToLower(), cancellationToken);

        if (nameExists)
            return TypedResults.Conflict($"A pipeline stage named '{name}' already exists.");

        if (request.Order > 0 && request.Order != stage.Order)
        {
            var orderExists = await db.PipelineStages
                .AnyAsync(p => p.Order == request.Order && p.Id != id, cancellationToken);

            if (orderExists)
                return TypedResults.Conflict($"A pipeline stage with order {request.Order} already exists.");

            stage.SetOrder(request.Order);
        }

        // Deactivating a stage that still holds leads hides them from the board without
        // moving them anywhere. Refuse and point the Admin at the impact endpoint, which
        // offers the reassignment targets.
        if (request.IsActive is false && stage.IsActive)
        {
            var usage = await usageService.GetStageUsageAsync(db, id, cancellationToken);

            if (usage.IsReferenced)
            {
                return TypedResults.Conflict(
                    $"'{stage.Name}' still holds {usage.LeadCount} lead{(usage.LeadCount == 1 ? "" : "s")}. " +
                    "Move them to another stage before deactivating it.");
            }

            if (usage.IsOnlyActiveStage)
            {
                return TypedResults.Conflict(
                    "This is the only active stage. A pipeline needs at least one active stage to accept leads.");
            }
        }

        stage.Rename(name);
        if (request.IsActive.HasValue) stage.SetActive(request.IsActive.Value);

        await db.SaveChangesAsync(cancellationToken);

        var response = new Response(stage.Id, stage.Name, stage.Order, stage.IsActive);
        return TypedResults.Ok(response);
    }
}
