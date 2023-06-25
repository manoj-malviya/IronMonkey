using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace IronMonkey.Api.Entities.Forms.Definitions;

public class FormDefinition {

    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; }
    public string Name { get; set; }
    public string Collection {get;set;}
    public List<FieldDefinition> Fields { get; set; } = new List<FieldDefinition>();
}