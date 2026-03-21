using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.CustomFields;

public class CreateCustomFieldEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/custom-fields", Handle)
        .WithSummary("Create a new custom field definition for the authenticated tenant")
        .WithTags("Custom Fields")
        .RequireAuthorization();

    public record Request(string FieldName, string FieldType, bool IsRequired, List<string>? Options);
    public record Response(Guid Id, string FieldName, string FieldType, bool IsRequired, List<string> Options);

    private static async Task<Results<Created<Response>, BadRequest<string>>> Handle(
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

        var field = CustomFieldDefinition.Create(tenantId, request.FieldName, fieldType, request.IsRequired, request.Options);
        db.CustomFieldDefinitions.Add(field);
        await db.SaveChangesAsync(cancellationToken);

        var response = new Response(field.Id, field.FieldName, field.FieldType.ToString(), field.IsRequired, field.Options);
        return TypedResults.Created($"/api/custom-fields/{field.Id}", response);
    }
}
