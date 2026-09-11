using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Configuration;

/// <summary>
/// Counts the records that would be affected by removing or changing a configuration item.
///
/// Deleting a stage or a field is not reversible from the UI, and the damage is invisible
/// until someone opens a record that lost its data. Every destructive configuration path
/// asks this first so the Admin is shown the blast radius before confirming, and so the
/// endpoint can refuse outright when the only safe answer is "reassign these first".
/// </summary>
public interface IConfigurationUsageService
{
    Task<StageUsage> GetStageUsageAsync(TenantDbContext db, Guid stageId, CancellationToken ct);

    Task<FieldUsage> GetFieldUsageAsync(TenantDbContext db, CustomFieldDefinition field, CancellationToken ct);
}

/// <param name="LeadCount">Leads currently sitting in the stage.</param>
/// <param name="IsOnlyActiveStage">
/// True when deactivating would leave the tenant with no active stage — a pipeline with
/// nowhere to put a lead cannot accept one.
/// </param>
public sealed record StageUsage(int LeadCount, bool IsOnlyActiveStage, int TransitionCount)
{
    public bool IsReferenced => LeadCount > 0;
}

/// <param name="RecordCount">Leads or contacts holding a value for this field.</param>
/// <param name="ValuesOutsideOptions">
/// Stored values that are no longer offered by the field's options, which is what makes an
/// option removal or a type change lossy.
/// </param>
public sealed record FieldUsage(int RecordCount, IReadOnlyList<string> ValuesOutsideOptions)
{
    public bool IsReferenced => RecordCount > 0;
}

public sealed class ConfigurationUsageService : IConfigurationUsageService
{
    public async Task<StageUsage> GetStageUsageAsync(TenantDbContext db, Guid stageId, CancellationToken ct)
    {
        var leadCount = await db.Leads.CountAsync(l => l.PipelineStageId == stageId, ct);

        var activeStageCount = await db.PipelineStages.CountAsync(s => s.IsActive, ct);
        var thisStageActive = await db.PipelineStages
            .AnyAsync(s => s.Id == stageId && s.IsActive, ct);

        var transitionCount = await db.StageTransitions
            .CountAsync(t => t.FromStageId == stageId || t.ToStageId == stageId, ct);

        return new StageUsage(
            leadCount,
            IsOnlyActiveStage: thisStageActive && activeStageCount <= 1,
            transitionCount);
    }

    public async Task<FieldUsage> GetFieldUsageAsync(
        TenantDbContext db, CustomFieldDefinition field, CancellationToken ct)
    {
        // Values are keyed by the definition Id inside the jsonb bag's "Values" object.
        var key = field.Id.ToString();

        // The table is chosen from an enum, never from caller input, so it cannot be injected.
        var table = field.AppliesTo == CustomFieldEntity.Lead ? "leads" : "contacts";

        // A non-null value under this key means the record actually captured something — an
        // absent key or an explicit null is not usage. jsonb_exists() rather than the `?`
        // operator, which Npgsql would parse as a positional parameter placeholder.
        //
        // TenantId is filtered explicitly: raw SQL bypasses the global query filters, and
        // several tenants can share a database, so without it another tenant's records would
        // be counted against this field.
        var sql = $@"
            SELECT count(*) AS record_count,
                   COALESCE(jsonb_agg(DISTINCT v), '[]'::jsonb)::text AS distinct_values
            FROM (
                SELECT custom_field_values -> 'Values' -> @key AS v
                FROM {table}
                WHERE ""TenantId"" = @tenantId
                  AND NOT ""IsDeleted""
                  AND jsonb_exists(custom_field_values -> 'Values', @key)
                  AND jsonb_typeof(custom_field_values -> 'Values' -> @key) <> 'null'
            ) s";

        var (recordCount, distinctJson) = await ExecuteUsageQueryAsync(db, sql, key, db.TenantId, ct);

        var outside = ExtractValuesOutsideOptions(field, distinctJson);

        return new FieldUsage(recordCount, outside);
    }

    /// <summary>
    /// Flattens the distinct stored values and returns those the field's options no longer
    /// offer. MultiSelect stores arrays, so each element is checked individually.
    /// </summary>
    private static List<string> ExtractValuesOutsideOptions(CustomFieldDefinition field, string distinctJson)
    {
        if (field.FieldType is not (CustomFieldType.Dropdown or CustomFieldType.MultiSelect))
            return [];

        using var doc = System.Text.Json.JsonDocument.Parse(distinctJson);

        var stored = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            switch (element.ValueKind)
            {
                case System.Text.Json.JsonValueKind.String:
                    if (element.GetString() is { Length: > 0 } s) stored.Add(s);
                    break;

                case System.Text.Json.JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        if (item.ValueKind == System.Text.Json.JsonValueKind.String
                            && item.GetString() is { Length: > 0 } i)
                            stored.Add(i);
                    break;
            }
        }

        stored.ExceptWith(field.Options);
        return [.. stored.OrderBy(x => x, StringComparer.Ordinal)];
    }

    private static async Task<(int RecordCount, string DistinctJson)> ExecuteUsageQueryAsync(
        TenantDbContext db, string sql, string key, Guid tenantId, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        var keyParameter = command.CreateParameter();
        keyParameter.ParameterName = "key";
        keyParameter.Value = key;
        command.Parameters.Add(keyParameter);

        var tenantParameter = command.CreateParameter();
        tenantParameter.ParameterName = "tenantId";
        tenantParameter.Value = tenantId;
        command.Parameters.Add(tenantParameter);

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return (0, "[]");

            // count(*) is bigint in Postgres; GetInt32 would throw an invalid-cast.
            var count = reader.IsDBNull(0) ? 0 : (int)reader.GetInt64(0);
            var json = reader.IsDBNull(1) ? "[]" : reader.GetString(1);
            return (count, json);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
