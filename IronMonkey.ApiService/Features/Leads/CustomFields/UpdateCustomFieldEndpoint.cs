using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Configuration;
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

    /// <param name="MigrationStrategy">
    /// How to treat values the change would invalidate. Null (the default) refuses the change
    /// and reports what would break. "Clear" drops the offending values; "Keep" applies the
    /// change and leaves them in place, where forms surface them as needing re-entry.
    /// </param>
    public record Request(
        string FieldName,
        string FieldType,
        bool IsRequired,
        List<string>? Options,
        string? AppliesTo = null,
        int? DisplayOrder = null,
        string? FieldKey = null,
        string? HelpText = null,
        string? DefaultValue = null,
        string? MigrationStrategy = null);

    public record Response(Guid Id, string FieldName, string FieldKey, string FieldType, bool IsRequired,
        List<string> Options, string AppliesTo, int DisplayOrder, string? HelpText,
        string? DefaultValue, bool IsArchived, int ClearedValues);

    /// <summary>
    /// Returned when a change would invalidate stored data and no strategy was chosen. The
    /// client renders these counts and re-submits with an explicit MigrationStrategy.
    /// </summary>
    public record MigrationRequired(
        string Message,
        int AffectedRecords,
        List<string> ValuesOutsideOptions,
        List<string> AvailableStrategies);

    private static readonly string[] Strategies = ["Clear", "Keep"];

    private static async Task<Results<Ok<Response>, NotFound, BadRequest<string>, Conflict<MigrationRequired>>> Handle(
        Guid id,
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IConfigurationUsageService usageService,
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

        CustomFieldEntity? scope = null;
        if (!string.IsNullOrWhiteSpace(request.AppliesTo))
        {
            if (!Enum.TryParse<CustomFieldEntity>(request.AppliesTo, ignoreCase: true, out var parsedScope))
                return TypedResults.BadRequest($"Invalid AppliesTo '{request.AppliesTo}'. Valid values: {string.Join(", ", Enum.GetNames<CustomFieldEntity>())}");
            scope = parsedScope;
        }

        if (request.MigrationStrategy is not null
            && !Strategies.Contains(request.MigrationStrategy, StringComparer.OrdinalIgnoreCase))
        {
            return TypedResults.BadRequest(
                $"Invalid migration strategy '{request.MigrationStrategy}'. Valid values: {string.Join(", ", Strategies)}");
        }

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var field = await db.CustomFieldDefinitions
            .SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (field is null) return TypedResults.NotFound();

        var targetScope = scope ?? field.AppliesTo;

        var duplicate = await db.CustomFieldDefinitions
            .AnyAsync(f => f.Id != id && f.AppliesTo == targetScope
                        && f.FieldName.ToLower() == name.ToLower(), cancellationToken);
        if (duplicate)
            return TypedResults.BadRequest($"A '{targetScope}' field named '{name}' already exists.");

        var key = string.IsNullOrWhiteSpace(request.FieldKey)
            ? field.FieldKey
            : request.FieldKey.Trim();

        if (!IsValidKey(key))
            return TypedResults.BadRequest("Internal key may contain only letters, digits and underscores.");

        var keyTaken = await db.CustomFieldDefinitions
            .AnyAsync(f => f.Id != id && f.AppliesTo == targetScope && f.FieldKey == key, cancellationToken);
        if (keyTaken)
            return TypedResults.BadRequest($"A '{targetScope}' field with the internal key '{key}' already exists.");

        // A type change, a scope change, or a narrowed option list can all strand values that
        // are already stored. Find out before writing, and refuse unless the Admin has said
        // what should happen to them.
        var typeChanged = field.FieldType != fieldType;
        var scopeChanged = field.AppliesTo != targetScope;
        var removedOptions = field.Options.Except(options, StringComparer.Ordinal).Any();

        var clearedValues = 0;

        if (typeChanged || scopeChanged || removedOptions)
        {
            var usage = await usageService.GetFieldUsageAsync(db, field, cancellationToken);

            if (usage.IsReferenced)
            {
                if (request.MigrationStrategy is null)
                {
                    return TypedResults.Conflict(new MigrationRequired(
                        BuildMigrationMessage(field, usage.RecordCount, typeChanged, scopeChanged, removedOptions),
                        usage.RecordCount,
                        [.. usage.ValuesOutsideOptions],
                        [.. Strategies]));
                }

                if (string.Equals(request.MigrationStrategy, "Clear", StringComparison.OrdinalIgnoreCase))
                {
                    clearedValues = await ClearStoredValuesAsync(db, field, cancellationToken);
                }
            }
        }

        // Renaming is safe: values are keyed by this definition's Id, not its name.
        field.Update(name, fieldType, request.IsRequired, options, scope, request.DisplayOrder,
            key, Trim(request.HelpText), Trim(request.DefaultValue));

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(field.Id, field.FieldName, field.FieldKey,
            field.FieldType.ToString(), field.IsRequired, field.Options, field.AppliesTo.ToString(),
            field.DisplayOrder, field.HelpText, field.DefaultValue, field.IsArchived, clearedValues));
    }

    private static string BuildMigrationMessage(
        CustomFieldDefinition field, int count, bool typeChanged, bool scopeChanged, bool removedOptions)
    {
        var reasons = new List<string>();
        if (typeChanged) reasons.Add("changing its type");
        if (scopeChanged) reasons.Add("changing which record it applies to");
        if (removedOptions) reasons.Add("removing options that are in use");

        var records = $"{count} record{(count == 1 ? "" : "s")}";

        return $"'{field.FieldName}' holds values on {records}. " +
               $"{char.ToUpperInvariant(reasons[0][0])}{reasons[0][1..]}" +
               (reasons.Count > 1 ? $" and {string.Join(" and ", reasons.Skip(1))}" : "") +
               " would make those values invalid. Choose how to handle them.";
    }

    /// <summary>
    /// Removes this field's key from every record's value bag. Done in SQL because the values
    /// live in a jsonb column that EF maps as an opaque blob — loading every lead to edit one
    /// key would be far more work and would churn the activity log.
    /// </summary>
    private static async Task<int> ClearStoredValuesAsync(
        TenantDbContext db, CustomFieldDefinition field, CancellationToken ct)
    {
        var table = field.AppliesTo == CustomFieldEntity.Lead ? "leads" : "contacts";

        // TenantId is filtered explicitly: raw SQL bypasses the global query filters, so
        // without it this would strip the field's values from every tenant sharing the
        // database, not just this one.
        var sql = $@"
            UPDATE {table}
            SET custom_field_values = jsonb_set(
                    custom_field_values,
                    '{{Values}}',
                    (custom_field_values -> 'Values') - @key)
            WHERE ""TenantId"" = @tenantId
              AND NOT ""IsDeleted""
              AND jsonb_exists(custom_field_values -> 'Values', @key)";

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        var keyParameter = command.CreateParameter();
        keyParameter.ParameterName = "key";
        keyParameter.Value = field.Id.ToString();
        command.Parameters.Add(keyParameter);

        var tenantParameter = command.CreateParameter();
        tenantParameter.ParameterName = "tenantId";
        tenantParameter.Value = db.TenantId;
        command.Parameters.Add(tenantParameter);

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            return await command.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
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
