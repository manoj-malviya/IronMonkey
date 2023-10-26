namespace IronMonkey.Api.Domain.Forms.Definitions;

public class FieldValidationRule
{
    public string Property {get; set;}
    public string Type { get; set; }
    public string Value { get; set; }
    public string Message { get; set; } = String.Empty;

    public FieldValidationRule(string property, string type, string value)
    {
        Property = property;
        Type = type;
        Value = value;
    }
}

public static class ValidationRuleType {
    public static readonly string Required = "required";
}