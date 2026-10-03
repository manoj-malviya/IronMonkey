using System.Text;
using System.Text.Json;

namespace IronMonkey.Web.Insights;

// Wire shapes for search, views, reports and exports. Mirrors the API contracts.

public sealed record SearchHit(Guid Id, string Type, string Title, string? Subtitle, string? Matched, double Score);
public sealed record SearchGroup(string Type, List<SearchHit> Hits, int Count, bool CountIsCapped, int Page, bool HasMore);
public sealed record SearchResult(string Query, List<SearchGroup> Groups, bool TimedOut);

public sealed record FieldInfo(string Key, string Label, string Kind, bool Sortable, bool Groupable, List<string>? Options, List<string> Operators);

public sealed class FilterCondition
{
    public string Field { get; set; } = "";
    public string Op { get; set; } = "";
    public JsonElement? Value { get; set; }
}

public sealed class FilterGroup
{
    public string Logic { get; set; } = "and";
    public List<FilterCondition> Conditions { get; set; } = [];
    public List<FilterGroup> Groups { get; set; } = [];
}

public sealed class SortSpec
{
    public string Field { get; set; } = "";
    public bool Descending { get; set; }
}

public sealed class ViewDefinition
{
    public FilterGroup Filter { get; set; } = new();
    public List<string> Columns { get; set; } = [];
    public SortSpec? Sort { get; set; }
}

public sealed record ViewColumn(string Key, string Label, string Kind);
public sealed record ViewRow(Guid Id, List<string?> Values);
public sealed record ViewResult(List<ViewColumn> Columns, List<ViewRow> Rows, int TotalCount, int Page, int PageSize, int TotalPages);
public sealed record QueryProblems(string Message, List<string> Problems);

public sealed record SavedView(Guid Id, string Name, string RecordType, ViewDefinition Definition, bool IsShared,
    bool IsOwner, bool IsDefault, string OwnerName, List<string> Problems);

public sealed class MeasureSpec
{
    public string Fn { get; set; } = "count";
    public string? Field { get; set; }
}

public sealed class DateRangeSpec
{
    public string Field { get; set; } = "createdAt";
    public string? Preset { get; set; }
}

public sealed class ReportSpec
{
    public FilterGroup Filter { get; set; } = new();
    public string? GroupBy { get; set; }
    public string? TimeField { get; set; }
    public string? TimeGranularity { get; set; }
    public DateRangeSpec? DateRange { get; set; }
    public List<MeasureSpec> Measures { get; set; } = [new()];
}

public sealed record ReportRow(string? Group, string? GroupLabel, DateTime? Bucket, List<decimal?> Values);
public sealed record ReportResult(string? GroupBy, string? TimeField, string? TimeGranularity, List<string> MeasureLabels,
    List<ReportRow> Rows, bool Truncated, int GroupCap, string TimeZone, DateTime GeneratedAt);

public sealed record ReportDefinition(Guid Id, string Name, string RecordType, ReportSpec Spec, bool IsShared, bool IsOwner,
    string Schedule, DateTime? LastRunAt, List<string> Problems);

public sealed record ExportJob(Guid Id, string RecordType, string Source, string Description, string Status,
    int? RowCount, bool WasTruncated, string? Error, DateTime CreatedAt, DateTime? CompletedAt, DateTime ExpiresAt, bool CanDownload);

public static class InsightUrls
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A view definition as a URL-safe token, so filter state lives in the URL and
    /// Back/Forward moves between filter states.</summary>
    public static string Encode(ViewDefinition definition) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(definition, Json)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static ViewDefinition? Decode(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8000) return null;
        try
        {
            var b64 = token.Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            return JsonSerializer.Deserialize<ViewDefinition>(Encoding.UTF8.GetString(Convert.FromBase64String(b64)), Json);
        }
        catch (Exception e) when (e is FormatException or JsonException) { return null; }
    }

    public static string DetailUrl(string type, Guid id, string? leadId = null) => type switch
    {
        "Lead" => $"/admin/leads/{id}",
        "Contact" => $"/admin/contacts/{id}",
        "Opportunity" => $"/admin/opportunities/{id}",
        "Task" when Guid.TryParse(leadId, out var lead) => $"/admin/leads/{lead}",
        _ => "/admin"
    };

    /// <summary>Builds a condition value from the text a user typed, shaped for the operator.</summary>
    public static JsonElement? ValueFor(string kind, string op, string? text)
    {
        if (op is "is_empty" or "is_not_empty" or "is_true" or "is_false" or "is_me") return null;
        if (op == "in_range") return JsonSerializer.SerializeToElement(new { preset = string.IsNullOrWhiteSpace(text) ? "thismonth" : text });
        if (op == "in") return JsonSerializer.SerializeToElement((text ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        if (op == "between")
        {
            var parts = (text ?? "").Split(['-', ','], 2, StringSplitOptions.TrimEntries);
            decimal.TryParse(parts.ElementAtOrDefault(0), out var min);
            decimal.TryParse(parts.ElementAtOrDefault(1), out var max);
            return JsonSerializer.SerializeToElement(new { min, max });
        }
        if (kind == "Number" && decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var n))
            return JsonSerializer.SerializeToElement(n);
        return JsonSerializer.SerializeToElement(text ?? "");
    }

    /// <summary>The text shown in a value box for an existing condition.</summary>
    public static string TextFor(JsonElement? value) => value switch
    {
        null => "",
        { ValueKind: JsonValueKind.String } v => v.GetString() ?? "",
        { ValueKind: JsonValueKind.Array } v => string.Join(", ", v.EnumerateArray().Select(e => e.ToString())),
        { ValueKind: JsonValueKind.Object } v when v.TryGetProperty("preset", out var p) => p.GetString() ?? "",
        { ValueKind: JsonValueKind.Object } v when v.TryGetProperty("min", out var mn) && v.TryGetProperty("max", out var mx) => $"{mn}-{mx}",
        { } v => v.ToString()
    };
}
