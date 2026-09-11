using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronMonkey.ApiService.Features.Leads.Pipeline.Routing;

/// <summary>
/// The structured form of a territory map, so the UI can edit rules as named rows instead of
/// asking an Admin to hand-write JSON.
///
/// The stored column keeps the flat <c>{"value": "agent-guid"}</c> shape that
/// <see cref="LeadRoutingService"/> already reads, so this is a view over existing data rather
/// than a migration: rules round-trip to that shape, and a map written before this existed
/// still parses into one rule per entry.
/// </summary>
public sealed class TerritoryRuleSet
{
    [JsonPropertyName("rules")]
    public List<TerritoryRule> Rules { get; set; } = [];

    /// <summary>
    /// Reads either the structured shape or the original flat map. Returns null when the JSON
    /// is neither, so callers can report a parse failure rather than silently routing nothing.
    /// </summary>
    public static TerritoryRuleSet? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new TerritoryRuleSet();

        try
        {
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            // Structured form: {"rules": [...]}
            if (doc.RootElement.TryGetProperty("rules", out var rulesElement))
            {
                if (rulesElement.ValueKind != JsonValueKind.Array) return null;

                var set = new TerritoryRuleSet();
                foreach (var element in rulesElement.EnumerateArray())
                {
                    var rule = ParseRule(element);
                    if (rule is null) return null;
                    set.Rules.Add(rule);
                }
                return set;
            }

            // Legacy flat form: {"Web": "guid", "Referral": "guid"}
            var legacy = new TerritoryRuleSet();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String) return null;

                legacy.Rules.Add(new TerritoryRule
                {
                    Name = property.Name,
                    Values = [property.Name],
                    AssigneeId = property.Value.GetString() ?? string.Empty
                });
            }
            return legacy;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TerritoryRule? ParseRule(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        var rule = new TerritoryRule();

        if (element.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            rule.Name = name.GetString() ?? string.Empty;

        if (element.TryGetProperty("assigneeId", out var assignee) && assignee.ValueKind == JsonValueKind.String)
            rule.AssigneeId = assignee.GetString() ?? string.Empty;

        if (element.TryGetProperty("values", out var values))
        {
            if (values.ValueKind != JsonValueKind.Array) return null;

            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String) return null;
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text)) rule.Values.Add(text.Trim());
            }
        }

        return rule;
    }

    /// <summary>
    /// Flattens back to the map the routing service reads. A rule matching several values
    /// contributes one entry per value.
    /// </summary>
    public Dictionary<string, string> ToLookup()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rule in Rules)
            foreach (var value in rule.Values)
                map[value] = rule.AssigneeId;

        return map;
    }

    /// <summary>
    /// Checks the rules make sense against the tenant's real users. Returns one readable
    /// message per problem — the UI shows these next to the rule rather than as a JSON error.
    /// </summary>
    public List<string> Validate(IReadOnlyCollection<Guid> validAssigneeIds)
    {
        var errors = new List<string>();

        if (Rules.Count == 0)
        {
            errors.Add("Add at least one territory rule, or switch the strategy to Round Robin.");
            return errors;
        }

        var seenValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < Rules.Count; i++)
        {
            var rule = Rules[i];
            var label = string.IsNullOrWhiteSpace(rule.Name) ? $"Rule {i + 1}" : rule.Name.Trim();

            if (string.IsNullOrWhiteSpace(rule.Name))
                errors.Add($"Rule {i + 1} needs a territory name.");

            if (rule.Values.Count == 0)
                errors.Add($"'{label}' needs at least one matching value.");

            if (!Guid.TryParse(rule.AssigneeId, out var assigneeId) || assigneeId == Guid.Empty)
            {
                errors.Add($"'{label}' needs an assigned user.");
            }
            else if (!validAssigneeIds.Contains(assigneeId))
            {
                errors.Add($"'{label}' is assigned to a user who no longer exists in this organisation.");
            }

            // Two rules claiming the same value make routing depend on ordering, which is not
            // something the editor exposes — so it is rejected rather than silently resolved.
            foreach (var value in rule.Values)
            {
                if (seenValues.TryGetValue(value, out var owner))
                    errors.Add($"'{value}' is claimed by both '{owner}' and '{label}'.");
                else
                    seenValues[value] = label;
            }
        }

        return errors;
    }

    public string ToJson() => JsonSerializer.Serialize(this);
}

/// <param name="Values">The lead-source or custom-field values this territory matches.</param>
public sealed class TerritoryRule
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("values")]
    public List<string> Values { get; set; } = [];

    [JsonPropertyName("assigneeId")]
    public string AssigneeId { get; set; } = string.Empty;
}
