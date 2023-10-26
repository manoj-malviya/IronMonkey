using IronMonkey.Api.Domain.Forms.Definitions;

namespace IronMonkey.Api.Domain.Forms.Definitions;
public class FieldDefinition {
    public string Name { get; set; }
    public FieldType FieldType { get; set; }
    public List<FieldValidationRule> Validators {get; set; } = new();

    public FieldDefinition(string name, FieldType fieldType, List<FieldValidationRule> validators) {
        Name = name;
        FieldType = fieldType;
        Validators = validators ?? Validators;
    }
}