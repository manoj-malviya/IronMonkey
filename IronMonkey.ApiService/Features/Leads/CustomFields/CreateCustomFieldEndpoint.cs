using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
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

    /// <param name="FieldKey">
    /// Optional stable key for integrations. Derived from the label when omitted.
    /// </param>
    public record Request(
        string FieldName,
        string FieldType,
        bool IsRequired,
        List<string>? Options,
        string? AppliesTo = null,
        int DisplayOrder = 0,
        string? FieldKey = null,
        string? HelpText = null,
        string? DefaultValue = null);

    public record Response(Guid Id, string FieldName, string FieldKey, string FieldType, bool IsRequired,
        List<string> Options, string AppliesTo, int DisplayOrder, string? HelpText,
        string? DefaultValue, bool IsArchived);

    private static async Task<Results<Created<Response>, BadRequest<string>>> Handle(
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var name = request.FieldName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return TypedResults.BadRequest("Field label must be between 1 and 100 characters.");

        if (!Enum.TryParse<CustomFieldType>(request.FieldType, ignoreCase: true, out var fieldType))
            return TypedResults.BadRequest($"Invalid field type '{request.FieldType}'. Valid values: {string.Join(", ", Enum.GetNames<CustomFieldType>())}");

        var options = NormaliseOptions(request.Options);

        if (fieldType is CustomFieldType.Dropdown or CustomFieldType.MultiSelect)
        {
            if (options.Count == 0)
                return TypedResults.BadRequest($"Options are required for field type '{fieldType}'.");

            if (options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Count)
                return TypedResults.BadRequest("Options must be unique.");
        }

        var scope = CustomFieldEntity.Lead;
        if (!string.IsNullOrWhiteSpace(request.AppliesTo)
            && !Enum.TryParse(request.AppliesTo, ignoreCase: true, out scope))
        {
            return TypedResults.BadRequest($"Invalid AppliesTo '{request.AppliesTo}'. Valid values: {string.Join(", ", Enum.GetNames<CustomFieldEntity>())}");
        }

        var key = string.IsNullOrWhiteSpace(request.FieldKey)
            ? CustomFieldDefinition.DeriveKey(name)
            : request.FieldKey.Trim();

        if (!IsValidKey(key))
            return TypedResults.BadRequest("Internal key may contain only letters, digits and underscores.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        // Two fields with the same name on the same record type are indistinguishable on a
        // form, so reject rather than let the operator guess which is which.
        var duplicate = await db.CustomFieldDefinitions
            .AnyAsync(f => f.AppliesTo == scope && f.FieldName.ToLower() == name.ToLower(), cancellationToken);
        if (duplicate)
            return TypedResults.BadRequest($"A '{scope}' field named '{name}' already exists.");

        var keyTaken = await db.CustomFieldDefinitions
            .AnyAsync(f => f.AppliesTo == scope && f.FieldKey == key, cancellationToken);
        if (keyTaken)
            return TypedResults.BadRequest($"A '{scope}' field with the internal key '{key}' already exists.");

        var displayOrder = request.DisplayOrder;
        if (displayOrder <= 0)
        {
            // Append rather than pile every new field at position 0, where the order among
            // them is then decided by the name tie-breaker.
            var maxOrder = await db.CustomFieldDefinitions
                .Where(f => f.AppliesTo == scope)
                .Select(f => (int?)f.DisplayOrder)
                .MaxAsync(cancellationToken) ?? 0;

            displayOrder = maxOrder + 1;
        }

        var field = CustomFieldDefinition.Create(tenantId, name, fieldType, request.IsRequired,
            options, scope, displayOrder, key, Trim(request.HelpText), Trim(request.DefaultValue));

        db.CustomFieldDefinitions.Add(field);
        await db.SaveChangesAsync(cancellationToken);

        var response = new Response(field.Id, field.FieldName, field.FieldKey, field.FieldType.ToString(),
            field.IsRequired, field.Options, field.AppliesTo.ToString(), field.DisplayOrder,
            field.HelpText, field.DefaultValue, field.IsArchived);

        return TypedResults.Created($"/api/custom-fields/{field.Id}", response);
    }

    private static List<string> NormaliseOptions(List<string>? options)
        => options is null
            ? []
            : [.. options.Select(o => o?.Trim() ?? string.Empty).Where(o => o.Length > 0)];

    private static bool IsValidKey(string key)
        => key.Length is > 0 and <= 100 && key.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
