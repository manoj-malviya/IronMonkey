using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Contacts;

public class GetContactEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/contacts/{id:guid}", Handle)
        .WithSummary("Get a single contact with its opportunities")
        .WithTags("Contacts")
        .RequireAuthorization();

    public record OpportunitySummary(Guid Id, string Title, string Stage, decimal Amount, DateTime ExpectedCloseDate);
    public record Response(
        Guid Id, string Name, string Email, string Mobile, DateTime CreatedAt,
        List<OpportunitySummary> Opportunities,
        Dictionary<string, object?> CustomFields);

    private static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var contact = await db.Contacts.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
            return TypedResults.NotFound();

        var opportunities = await db.Opportunities
            .Where(o => o.ContactId == id)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OpportunitySummary(o.Id, o.Title, o.Stage, o.Amount, o.ExpectedCloseDate))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new Response(
            contact.Id, contact.Name, contact.Email, contact.Mobile, contact.CreatedAt,
            opportunities, contact.CustomFields.Values));
    }
}
