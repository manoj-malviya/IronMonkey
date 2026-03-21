using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Authentication.Services;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class ProvisionTenantEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/tenants/{id}/provision", Handle)
        .WithSummary("Provision an approved tenant — creates isolated PostgreSQL database with schema and seed data")
        .WithTags("Platform Admin");

    public record Response(Guid TenantId, string Message);

    private static async Task<Results<Ok<Response>, BadRequest<string>, NotFound>> Handle(
        Guid id,
        ITenantProvisioningService provisioningService,
        CancellationToken cancellationToken)
    {
        try
        {
            await provisioningService.ProvisionTenantAsync(id, cancellationToken);
            return TypedResults.Ok(new Response(id, "Tenant provisioned successfully."));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }
}
