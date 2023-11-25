using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace IronMonkey.Api.Entities.Customers;

public class Customer {
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; }
    public string Name {get;set;}
    public string Mobile {get;set;}
}