using IronMonkey.Api.Entities.Forms;

namespace IronMonkey.Api.Entities.Records;

public class Field {
    public string Name { get; set;}
    public string Value { get; set;}
    public FieldType FieldType { get; set;}
}