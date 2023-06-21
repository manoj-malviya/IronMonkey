using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace IronMonkey.Api.Entities.Leads.Definitions;

public class Record {

    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; }
    public string Name { get; set; }
    public List<FieldDefinition> Fields { get; set; } = new List<FieldDefinition>();
}