using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace  IronMonkey.Api.Entities.Leads.Definitions;

public class FieldDefinition {
    public string Name { get; set; }
    public FieldType FieldType { get; set; }

    public bool IsRequired { get; set; } = false;

    public FieldDefinition(string name, FieldType fieldType) {
        Name = name;
        FieldType = fieldType;
    }
}