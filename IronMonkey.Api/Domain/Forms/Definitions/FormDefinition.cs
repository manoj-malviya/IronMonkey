using MongoDB.Bson;

namespace IronMonkey.Api.Domain.Forms.Definitions;

public class FormDefinition {

    public ObjectId Id { get; set; }
    public string Name { get; set; } = String.Empty;
    public string Collection {get;set;} = String.Empty;
    public List<FieldDefinition> Fields { get; set; } = new List<FieldDefinition>();
}