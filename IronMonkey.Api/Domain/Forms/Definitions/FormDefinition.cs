using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace IronMonkey.Api.Domain.Forms.Definitions;

public class FormDefinition {

    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; }
    public string Name { get; set; } = String.Empty;
    public string Collection {get;set;} = String.Empty;
    public List<FieldDefinition> Fields { get; set; } = new List<FieldDefinition>();
}