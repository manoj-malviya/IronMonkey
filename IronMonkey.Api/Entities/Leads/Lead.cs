using IronMonkey.Api.Entities.Customers;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace IronMonkey.Api.Entities.Leads;

public class Lead
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id{ get; set; }
    public Customer Customer{get; set;}
    public LeadType LeadType{get; set; }

    public LeadDetail Detail{get; set;}

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt   { get; set; }
    public int CreatedBy {get; set; }
    public int LastUpdatedBy {get; set; }
}

public class LeadType
{
    public int Id{ get; set;}
    public string Name{ get; set; }
}