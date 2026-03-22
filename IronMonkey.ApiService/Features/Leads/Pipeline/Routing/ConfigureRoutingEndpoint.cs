using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Pipeline.Routing;

public class ConfigureRoutingEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/routing-config", Handle)
        .WithSummary("Configure lead routing strategy for the tenant (one strategy per pipeline, per D-15)")
        .WithTags("Routing")
        .RequireAuthorization();

    public record Request(
        string Strategy,
        string Dimension,
        string? TerritoryMapJson,
        string? CustomFieldKey);

    public record Response(Guid Id, string Strategy, string Dimension, bool IsEnabled);

    private static async Task<Results<Ok<Response>, BadRequest<string>>> Handle(
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<RoutingStrategy>(request.Strategy, ignoreCase: true, out var strategy))
            return TypedResults.BadRequest($"Invalid strategy. Valid: {string.Join(", ", Enum.GetNames<RoutingStrategy>())}");

        if (!Enum.TryParse<RoutingDimension>(request.Dimension, ignoreCase: true, out var dimension))
            return TypedResults.BadRequest($"Invalid dimension. Valid: {string.Join(", ", Enum.GetNames<RoutingDimension>())}");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        // One routing config per tenant (D-15) — upsert
        var existing = await db.RoutingConfigs
            .FirstOrDefaultAsync(r => r.TenantId == tenantId, cancellationToken);

        if (existing != null)
        {
            existing.Update(strategy, dimension, request.TerritoryMapJson, request.CustomFieldKey);
            existing.Enable();
        }
        else
        {
            existing = RoutingConfig.Create(tenantId, strategy, dimension,
                request.TerritoryMapJson, request.CustomFieldKey);
            db.RoutingConfigs.Add(existing);
        }

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(existing.Id, existing.Strategy.ToString(),
            existing.Dimension.ToString(), existing.IsEnabled));
    }
}
