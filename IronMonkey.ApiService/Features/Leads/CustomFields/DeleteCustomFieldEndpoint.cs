using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Configuration;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.CustomFields;

public class DeleteCustomFieldEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapDelete("/api/custom-fields/{id:guid}", Handle)
        .WithSummary("Delete an unused custom field, or archive one that holds values")
        .WithTags("Custom Fields")
        .RequireAuthorization();

    /// <param name="Archived">True when the field was archived rather than deleted.</param>
    public record Response(bool Success, string? Message, bool Archived, int RecordCount);

    /// <param name="archive">
    /// Set by the client once the Admin has seen the impact and accepted archiving. Without
    /// it a field that holds values is refused, so data is never quietly detached. Nullable
    /// because a non-nullable bool is a *required* query parameter in minimal APIs.
    /// </param>
    private static async Task<Results<Ok<Response>, NotFound, Conflict<string>>> Handle(
        Guid id,
        bool? archive,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IConfigurationUsageService usageService,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var field = await db.CustomFieldDefinitions
            .SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (field is null) return TypedResults.NotFound();

        var usage = await usageService.GetFieldUsageAsync(db, field, cancellationToken);

        if (usage.IsReferenced)
        {
            if (archive != true)
            {
                return TypedResults.Conflict(
                    $"'{field.FieldName}' holds values on {usage.RecordCount} " +
                    $"record{(usage.RecordCount == 1 ? "" : "s")}. Archive it instead to keep that data, " +
                    "or clear the values first.");
            }

            // Archiving keeps the definition so the stored values still resolve to a name and
            // a type; the field simply stops rendering on forms.
            field.Archive();
            await db.SaveChangesAsync(cancellationToken);

            return TypedResults.Ok(new Response(
                true,
                $"Archived '{field.FieldName}'. Its values are preserved on {usage.RecordCount} " +
                $"record{(usage.RecordCount == 1 ? "" : "s")}.",
                Archived: true,
                usage.RecordCount));
        }

        db.CustomFieldDefinitions.Remove(field);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(true, null, Archived: false, 0));
    }
}
