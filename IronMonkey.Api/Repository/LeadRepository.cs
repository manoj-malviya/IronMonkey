using IronMonkey.Api.Entities.Leads;
using IronMonkey.Api.Infrastructures.MongoDb;
using MongoDB.Bson;
using MongoDB.Driver;

namespace IronMonkey.Api.Repository;

public class LeadRepository {
    private readonly IMongoCollection<BsonDocument> _collection;
    public LeadRepository(IMongoDbContext context) {
        var db = context.Client.GetDatabase(context.DatabaseName);
        this._collection = db.GetCollection<BsonDocument>("lead");
    }

    public async void Create(Lead lead) {
        var doc = ToDocument(lead);
        await _collection.InsertOneAsync(doc);
    }

    public async Task<Lead> GetLead(string id) {
        var filter = Builders<BsonDocument>.Filter.Eq("Id", id);

        var doc = await _collection.FindAsync(filter);

        return new Lead();
    }

    private static Lead ToLead(BsonDocument doc) {
        var lead = new Lead();
        
        // lead.LeadType = new LeadType(doc.GetElement(nameof(lead.LeadType)).Value.ToString());
        lead.CreatedAt = doc.GetElement(nameof(lead.CreatedAt)).Value.ToUniversalTime();
        lead.UpdatedAt = doc.GetElement(nameof(lead.UpdatedAt)).Value.ToUniversalTime();
        lead.CreatedBy = doc.GetElement(nameof(lead.CreatedBy)).Value.ToInt32();
        lead.LastUpdatedBy = doc.GetElement(nameof(lead.LastUpdatedBy)).Value.ToInt32();

        foreach (var element in doc.Elements) {

        }

        return lead;
    }
    private static BsonDocument ToDocument(Lead lead) {
        var doc = new BsonDocument{
            {nameof(lead.LeadType), lead.LeadType.Name},
            {nameof(lead.CreatedAt), lead.CreatedAt},
            {nameof(lead.UpdatedAt), lead.UpdatedAt},
            {nameof(lead.CreatedBy), lead.CreatedBy},
            {nameof(lead.LastUpdatedBy), lead.LastUpdatedBy},
        };
        
        var list = new List<BsonElement>();
        foreach (var field in lead.Detail.Fields) {
            list.Add(new BsonElement(nameof(field.Name), field.Value));
        }

        doc.AddRange(list);

        return doc;
    }
}