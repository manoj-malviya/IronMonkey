using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.CustomFields;

public class DeleteCustomFieldEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapDelete("/api/custom-fields/{id:guid}", Handle)
        .WithSummary("Delete a custom field definition for the authenticated tenant")
        .WithTags("Custom Fields")
        .RequireAuthorization();

    public record Response(bool Success, string? Message);

    private static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var field = await db.CustomFieldDefinitions
            .SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (field is null) return TypedResults.NotFound();

        db.CustomFieldDefinitions.Remove(field);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(true, null));
    }
}
