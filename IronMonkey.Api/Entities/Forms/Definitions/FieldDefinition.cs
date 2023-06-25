using IronMonkey.Api.Infrastructures.Validations;

namespace  IronMonkey.Api.Entities.Forms.Definitions;

public class FieldDefinition {
    public string Name { get; set; }
    public FieldType FieldType { get; set; }
    public ValidationRule[] Validators {get; set; }

    public FieldDefinition(string name, FieldType fieldType, ValidationRule[] validators) {
        Name = name;
        FieldType = fieldType;
        Validators = validators;
    }
}