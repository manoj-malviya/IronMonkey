namespace IronMonkey.Api.Infrastructures.Validations;

public class ValidationRule
{
    public string Property {get; set;}
    public string Type { get; set; }
    public string Value { get; set; }
    public string Message { get; set; }

    public ValidationRule(string property, string type, string value)
    {
        Property = property;
        Type = type;
        Value = value;
    }
}

public static class ValidationRuleType {
    public static readonly string Required = "required";
}