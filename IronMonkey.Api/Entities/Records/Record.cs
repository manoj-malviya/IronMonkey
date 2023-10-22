using IronMonkey.Api.Entities.Customers;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace IronMonkey.Api.Entities.Records;

public class Record
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id{ get; set; }
    public Customer Customer{get; set;}
    public RecordDetail Detail{get; set;}
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt   { get; set; }
    public int CreatedBy {get; set; }
    public int LastUpdatedBy {get; set; }
}