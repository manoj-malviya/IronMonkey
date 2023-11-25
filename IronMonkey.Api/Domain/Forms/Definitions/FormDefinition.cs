using MongoDB.Bson;

namespace IronMonkey.Api.Domain.Forms.Definitions;

public class FormDefinition {

    public ObjectId Id { get; set; }
    public string Name { get; set; } = String.Empty;
    public string Storage {get;set;} = String.Empty;
    public IEnumerable<FieldDefinition> FieldList { get; set; } = new List<FieldDefinition>();
}