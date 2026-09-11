using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.CustomFields;

public class ListCustomFieldsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/custom-fields", Handle)
        .WithSummary("List custom field definitions for the authenticated tenant")
        .WithTags("Custom Fields")
        .RequireAuthorization();

    public record Response(Guid Id, string FieldName, string FieldKey, string FieldType, bool IsRequired,
        List<string> Options, string AppliesTo, int DisplayOrder, string? HelpText,
        string? DefaultValue, bool IsArchived);

    /// <param name="includeArchived">
    /// Archived fields are hidden by default so forms never render a retired field. The
    /// configuration workspace passes true to list them for restore. Nullable because a
    /// non-nullable bool is a *required* query parameter in minimal APIs — omitting it would
    /// 400/500 every existing caller, including the lead and contact forms.
    /// </param>
    private static async Task<Ok<List<Response>>> Handle(
        string? appliesTo,
        bool? includeArchived,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var query = db.CustomFieldDefinitions.AsQueryable();

        // Forms ask for just the fields belonging to the record being edited.
        if (!string.IsNullOrWhiteSpace(appliesTo)
            && Enum.TryParse<CustomFieldEntity>(appliesTo, ignoreCase: true, out var scope))
        {
            query = query.Where(c => c.AppliesTo == scope);
        }

        if (includeArchived != true)
            query = query.Where(c => !c.IsArchived);

        var fields = await query
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.FieldName)
            .ToListAsync(cancellationToken);

        var response = fields
            .Select(f => new Response(f.Id, f.FieldName, f.FieldKey, f.FieldType.ToString(), f.IsRequired,
                f.Options, f.AppliesTo.ToString(), f.DisplayOrder, f.HelpText, f.DefaultValue, f.IsArchived))
            .ToList();

        return TypedResults.Ok(response);
    }
}
