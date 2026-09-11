using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Contacts;

public class DeleteContactEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapDelete("/api/contacts/{id:guid}", Handle)
        .WithSummary("Soft-delete a contact that has no opportunities")
        .WithTags("Contacts")
        .RequireAuthorization();

    public record Response(string Message);

    private static async Task<Results<Ok<Response>, ValidationError, NotFound>> Handle(
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

        // Opportunity.ContactId is required, so deleting the contact would leave deals
        // pointing at a row that no read path can load. Refuse rather than orphan them.
        var hasOpportunities = await db.Opportunities.AnyAsync(o => o.ContactId == id, cancellationToken);
        if (hasOpportunities)
            return new ValidationError("This contact has opportunities. Delete or reassign them first.");

        contact.IsDeleted = true;
        contact.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response("Contact deleted successfully."));
    }
}
