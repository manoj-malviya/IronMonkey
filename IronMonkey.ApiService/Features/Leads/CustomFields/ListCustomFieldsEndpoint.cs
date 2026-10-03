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
        string? DefaultValue, bool IsArchived,
        /// <summary>The pipeline this field is scoped to, or null for tenant-wide.</summary>
        Guid? PipelineId);

    /// <param name="includeArchived">
    /// Archived fields are hidden by default so forms never render a retired field. The
    /// configuration workspace passes true to list them for restore. Nullable because a
    /// non-nullable bool is a *required* query parameter in minimal APIs — omitting it would
    /// 400/500 every existing caller, including the lead and contact forms.
    /// </param>
    /// <param name="pipelineId">
    /// Scopes the list to the fields a record in this pipeline would show: the tenant-wide
    /// ones PLUS that pipeline's own. Custom fields are deliberately ADDITIVE rather than
    /// override-style — a field is captured data, and hiding the tenant-wide ones because a
    /// pipeline added one of its own would make values already stored unreachable. Omitted
    /// returns every field regardless of scope, which is what the configuration workspace
    /// wants and what every pre-existing caller gets.
    /// </param>
    internal static async Task<Ok<List<Response>>> Handle(
        string? appliesTo,
        bool? includeArchived,
        Guid? pipelineId,
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

        // Additive, per PipelineTargeting.ResolveAdditive: tenant-wide fields plus this
        // pipeline's. A field scoped to a DIFFERENT pipeline is excluded — that one really
        // does not apply here.
        if (pipelineId is { } scopeId)
            query = query.Where(c => c.PipelineId == null || c.PipelineId == scopeId);

        var fields = await query
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.FieldName)
            .ToListAsync(cancellationToken);

        var response = fields
            .Select(f => new Response(f.Id, f.FieldName, f.FieldKey, f.FieldType.ToString(), f.IsRequired,
                f.Options, f.AppliesTo.ToString(), f.DisplayOrder, f.HelpText, f.DefaultValue,
                f.IsArchived, f.PipelineId))
            .ToList();

        return TypedResults.Ok(response);
    }
}
