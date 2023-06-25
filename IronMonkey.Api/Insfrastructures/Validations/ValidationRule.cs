namespace IronMonkey.Api.Infrastructures.Validations;

public class ValidationRule
{

    public string Property {get; set}
    public string Type { get; set; }
    public object Value { get; set; }
    public string Message { get; set; }
}