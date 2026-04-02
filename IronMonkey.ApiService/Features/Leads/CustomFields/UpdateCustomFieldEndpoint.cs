using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.CustomFields;

public class UpdateCustomFieldEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/custom-fields/{id:guid}", Handle)
        .WithSummary("Update a custom field definition for the authenticated tenant")
        .WithTags("Custom Fields")
        .RequireAuthorization();

    public record Request(string FieldName, string FieldType, bool IsRequired, List<string>? Options);
    public record Response(Guid Id, string FieldName, string FieldType, bool IsRequired, List<string> Options);

    private static async Task<Results<Ok<Response>, NotFound, BadRequest<string>>> Handle(
        Guid id,
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<CustomFieldType>(request.FieldType, ignoreCase: true, out var fieldType))
            return TypedResults.BadRequest($"Invalid field type '{request.FieldType}'. Valid values: {string.Join(", ", Enum.GetNames<CustomFieldType>())}");

        if (fieldType is CustomFieldType.Dropdown or CustomFieldType.MultiSelect)
        {
            if (request.Options is null || request.Options.Count == 0)
                return TypedResults.BadRequest($"Options are required for field type '{fieldType}'.");
        }

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var field = await db.CustomFieldDefinitions
            .SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (field is null) return TypedResults.NotFound();

        field.Update(request.FieldName, fieldType, request.IsRequired, request.Options);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(field.Id, field.FieldName, field.FieldType.ToString(), field.IsRequired, field.Options));
    }
}
