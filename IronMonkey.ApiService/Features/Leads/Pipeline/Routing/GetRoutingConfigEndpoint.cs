using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.Pipeline.Routing;

public class GetRoutingConfigEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/routing-config", Handle)
        .WithSummary("Get the current routing configuration for the tenant")
        .WithTags("Routing")
        .RequireAuthorization();

    public record Response(Guid Id, string Strategy, string Dimension,
        string? TerritoryMapJson, string? CustomFieldKey, bool IsEnabled, int RoundRobinPointer);

    private static async Task<Results<Ok<Response>, NotFound>> Handle(
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var config = await db.RoutingConfigs
            .FirstOrDefaultAsync(r => r.TenantId == tenantId, cancellationToken);

        if (config == null) return TypedResults.NotFound();

        return TypedResults.Ok(new Response(config.Id, config.Strategy.ToString(), config.Dimension.ToString(),
            config.TerritoryMapJson, config.CustomFieldKey, config.IsEnabled, config.RoundRobinPointer));
    }
}
