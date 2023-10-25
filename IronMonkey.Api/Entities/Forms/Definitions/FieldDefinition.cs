using IronMonkey.Api.Infrastructures.Validations;

namespace  IronMonkey.Api.Entities.Forms.Definitions;

public class FieldDefinition {
    public string Name { get; set; }
    public FieldType FieldType { get; set; }
    public List<ValidationRule> Validators {get; set; } = new();

    public FieldDefinition(string name, FieldType fieldType, List<ValidationRule> validators) {
        Name = name;
        FieldType = fieldType;
        Validators = validators ?? Validators;
    }
}