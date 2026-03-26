using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Authentication.Services;
using IronMonkey.Data;

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
        CentralDbContext centralDb,
        ITenantProvisioningService provisioningService,
        CancellationToken cancellationToken)
    {
        var signupRequest = await centralDb.SignupRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (signupRequest is null)
            return TypedResults.NotFound();

        var recipeId = signupRequest.RecipeId;

        try
        {
            await provisioningService.ProvisionTenantAsync(id, recipeId, cancellationToken);
            return TypedResults.Ok(new Response(id, "Tenant provisioned successfully."));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }
}
