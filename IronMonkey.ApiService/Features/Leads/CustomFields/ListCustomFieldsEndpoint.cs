using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.CustomFields;

public class ListCustomFieldsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/custom-fields", Handle)
        .WithSummary("List all custom field definitions for the authenticated tenant")
        .WithTags("Custom Fields")
        .RequireAuthorization();

    public record Response(Guid Id, string FieldName, string FieldType, bool IsRequired, List<string> Options);

    private static async Task<Ok<List<Response>>> Handle(
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var fields = await db.CustomFieldDefinitions
            .OrderBy(c => c.FieldName)
            .ToListAsync(cancellationToken);

        var response = fields
            .Select(f => new Response(f.Id, f.FieldName, f.FieldType.ToString(), f.IsRequired, f.Options))
            .ToList();

        return TypedResults.Ok(response);
    }
}
