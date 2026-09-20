using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.CustomFields;

/// <summary>
/// Brings an archived field back onto forms. Archiving is the safe alternative to deleting a
/// field in use, so it has to be undoable — otherwise it is just a slower delete.
/// </summary>
public class RestoreCustomFieldEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/custom-fields/{id:guid}/restore", Handle)
        .WithSummary("Restore an archived custom field for the authenticated tenant")
        .WithTags("Custom Fields")
        .RequireAuthorization();

    public record Response(Guid Id, string FieldName, bool IsArchived);

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

        field.Restore();
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(field.Id, field.FieldName, field.IsArchived));
    }
}
