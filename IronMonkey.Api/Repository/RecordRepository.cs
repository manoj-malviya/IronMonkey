using IronMonkey.Api.Contracts;
using IronMonkey.Api.Data.MongoDb;
using IronMonkey.Api.Domain.Records;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;

namespace IronMonkey.Api.Repository;

public class RecordRepository : IRecordRepository {
    private readonly MongoDbContext db;
    public RecordRepository(MongoDbContext context) {
        this.db = context;
    }

    public async void Create(Record lead) {
        var doc = ToDocument(lead);
        await db.AddAsync(doc);
    }

    public async Task<Record> GetLead(string id) {
        var filter = Builders<BsonDocument>.Filter.Eq("Id", id);

        // var doc = await db.FindAsync()

        return new Record();
    }

    private static Record ToLead(BsonDocument doc) {
        var lead = new Record();
        
        // lead.LeadType = new LeadType(doc.GetElement(nameof(lead.LeadType)).Value.ToString());
        lead.CreatedAt = doc.GetElement(nameof(lead.CreatedAt)).Value.ToUniversalTime();
        lead.UpdatedAt = doc.GetElement(nameof(lead.UpdatedAt)).Value.ToUniversalTime();
        lead.CreatedBy = doc.GetElement(nameof(lead.CreatedBy)).Value.ToInt32();
        lead.LastUpdatedBy = doc.GetElement(nameof(lead.LastUpdatedBy)).Value.ToInt32();

        foreach (var element in doc.Elements) {

        }

        return lead;
    }
    private static BsonDocument ToDocument(Record lead) {
        var doc = new BsonDocument{
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