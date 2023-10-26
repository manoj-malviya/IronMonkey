namespace IronMonkey.Api.Domain.Records;
public class Field {
    public string Name { get; set;}
    public string Value { get; set;}
    public FieldType FieldType { get; set;}

    public Field(string name, string value, FieldType fieldType) {
        Name = name; 
        Value = value;
        FieldType = fieldType;
    }
}