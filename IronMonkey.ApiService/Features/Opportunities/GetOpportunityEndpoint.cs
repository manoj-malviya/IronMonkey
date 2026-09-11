using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Opportunities;

public class GetOpportunityEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/opportunities/{id:guid}", Handle)
        .WithSummary("Get a single opportunity by id")
        .WithTags("Opportunities")
        .RequireAuthorization();

    public record Response(
        Guid Id, string Title, Guid ContactId, string ContactName,
        string Stage, decimal Amount, DateTime ExpectedCloseDate,
        string? LossReason, DateTime CreatedAt);

    private static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var opportunity = await db.Opportunities
            .Include(o => o.Contact)
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (opportunity is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(new Response(
            opportunity.Id, opportunity.Title, opportunity.ContactId,
            opportunity.Contact?.Name ?? "—", opportunity.Stage, opportunity.Amount,
            opportunity.ExpectedCloseDate, opportunity.LossReason, opportunity.CreatedAt));
    }
}
