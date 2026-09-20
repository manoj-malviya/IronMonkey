using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Configuration;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.CustomFields;

/// <summary>
/// Reports how many records hold a value for a field, and which stored values its current
/// options no longer cover — the two things an Admin needs before archiving it or narrowing
/// its options.
/// </summary>
public class GetCustomFieldImpactEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/custom-fields/{id:guid}/impact", Handle)
        .WithSummary("Report the records affected by archiving or changing a custom field")
        .WithTags("Custom Fields")
        .RequireAuthorization();

    public record Response(
        Guid Id,
        string FieldName,
        string AppliesTo,
        int RecordCount,
        List<string> ValuesOutsideOptions,
        bool CanDelete);

    private static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id,
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

        return TypedResults.Ok(new Response(
            field.Id,
            field.FieldName,
            field.AppliesTo.ToString(),
            usage.RecordCount,
            [.. usage.ValuesOutsideOptions],
            // A field holding data can only be archived; deleting would orphan every value.
            CanDelete: !usage.IsReferenced));
    }
}
