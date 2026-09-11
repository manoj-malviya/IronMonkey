using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Opportunities;

public class DeleteOpportunityEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapDelete("/api/opportunities/{id:guid}", Handle)
        .WithSummary("Soft-delete an opportunity")
        .WithTags("Opportunities")
        .RequireAuthorization();

    public record Response(string Message);

    private static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var opportunity = await db.Opportunities.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (opportunity is null)
            return TypedResults.NotFound();

        opportunity.IsDeleted = true;
        opportunity.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response("Opportunity deleted successfully."));
    }
}
