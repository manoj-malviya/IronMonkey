namespace IronMonkey.Data.Entities;

public class CustomFieldValues
{
    public Dictionary<string, object?> Values { get; set; } = new();

    public void Set(string fieldId, object? value) => Values[fieldId] = value;

    public object? Get(string fieldId) => Values.TryGetValue(fieldId, out var val) ? val : null;
}
