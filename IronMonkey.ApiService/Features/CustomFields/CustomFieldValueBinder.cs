using System.Text.Json;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.CustomFields;

/// <summary>
/// Validates and normalises submitted custom-field values against a tenant's definitions.
///
/// Values arrive as loosely-typed JSON from a form post, but they are stored in a jsonb bag
/// that nothing else type-checks. Without this, a Number field happily stores "abc" and a
/// Dropdown stores an option that was never offered — the data is only as good as whatever
/// the last caller sent. Everything is keyed by the definition Id, not the field name, so
/// renaming a field keeps its captured values attached.
/// </summary>
public static class CustomFieldValueBinder
{
    public sealed record BindResult(Dictionary<string, object?> Values, List<string> Errors)
    {
        public bool IsValid => Errors.Count == 0;
    }

    public static BindResult Bind(
        IReadOnlyCollection<CustomFieldDefinition> definitions,
        Dictionary<string, object?>? submitted)
    {
        var values = new Dictionary<string, object?>();
        var errors = new List<string>();
        submitted ??= new Dictionary<string, object?>();

        foreach (var def in definitions)
        {
            var key = def.Id.ToString();

            // Accept the field name or the internal key as fallbacks so recipe-seeded leads
            // and the external ingestion API (both name-keyed) still resolve to the right
            // field.
            if (!submitted.TryGetValue(key, out var raw)
                && !submitted.TryGetValue(def.FieldName, out raw)
                && !string.IsNullOrEmpty(def.FieldKey))
            {
                submitted.TryGetValue(def.FieldKey, out raw);
            }

            var normalised = Normalise(raw);

            // An archived field no longer renders on a form, so nothing can supply it. Keep
            // whatever was already captured, but never fail a save on its behalf — otherwise
            // archiving a required field locks every record in the tenant.
            if (def.IsArchived)
            {
                if (normalised is not null)
                {
                    var (archivedValue, _) = Convert(def, normalised);
                    if (archivedValue is not null) values[key] = archivedValue;
                }
                continue;
            }

            // A default stands in when a create form submits nothing for the field. It goes
            // through the same conversion and option checks as a typed value, so a default
            // that no longer matches the options is reported rather than stored.
            if (normalised is null && !string.IsNullOrWhiteSpace(def.DefaultValue))
                normalised = Normalise(def.DefaultValue);

            if (normalised is null)
            {
                if (def.IsRequired)
                    errors.Add($"'{def.FieldName}' is required.");
                continue;
            }

            var (converted, error) = Convert(def, normalised);
            if (error is not null)
            {
                errors.Add(error);
                continue;
            }

            values[key] = converted;
        }

        return new BindResult(values, errors);
    }

    /// <summary>Unwraps JsonElement and treats blank strings as "not supplied".</summary>
    private static object? Normalise(object? raw)
    {
        if (raw is null) return null;

        if (raw is JsonElement el)
        {
            raw = el.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Number => el.TryGetDecimal(out var d) ? d : null,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array => el.EnumerateArray()
                    .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList(),
                _ => null
            };
        }

        if (raw is string s && string.IsNullOrWhiteSpace(s)) return null;
        if (raw is List<string?> list && list.Count == 0) return null;


        return raw;
    }

    private static (object? Value, string? Error) Convert(CustomFieldDefinition def, object value)
    {
        var text = value as string;

        switch (def.FieldType)
        {
            case CustomFieldType.Text:
                return (text ?? value.ToString(), null);

            case CustomFieldType.Number:
            case CustomFieldType.Currency:
                if (value is decimal dec) return (dec, null);
                if (decimal.TryParse(text ?? value.ToString(), out var parsedNumber))
                    return (parsedNumber, null);
                return (null, $"'{def.FieldName}' must be a number.");

            case CustomFieldType.Boolean:
                if (value is bool b) return (b, null);
                if (bool.TryParse(text, out var parsedBool)) return (parsedBool, null);
                return (null, $"'{def.FieldName}' must be true or false.");

            case CustomFieldType.Date:
                if (value is DateTime dt)
                    return (DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("yyyy-MM-dd"), null);
                if (DateTime.TryParse(text, out var parsedDate))
                    return (parsedDate.ToString("yyyy-MM-dd"), null);
                return (null, $"'{def.FieldName}' must be a valid date.");

            case CustomFieldType.Dropdown:
                var choice = text ?? value.ToString();
                if (!def.Options.Contains(choice))
                    return (null, $"'{choice}' is not a valid option for '{def.FieldName}'.");
                return (choice, null);

            case CustomFieldType.MultiSelect:
                var selected = value switch
                {
                    List<string?> l => l.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList(),
                    string single => [single],
                    _ => new List<string>()
                };
                var invalid = selected.Where(x => !def.Options.Contains(x)).ToList();
                if (invalid.Count > 0)
                    return (null, $"'{string.Join(", ", invalid)}' {(invalid.Count == 1 ? "is not a valid option" : "are not valid options")} for '{def.FieldName}'.");
                return (selected, null);

            default:
                return (text ?? value.ToString(), null);
        }
    }
}
