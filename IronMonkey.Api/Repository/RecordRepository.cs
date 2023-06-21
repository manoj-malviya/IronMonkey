using IronMonkey.Api.Contracts;
using IronMonkey.Api.Entities.Leads.Definitions;
using IronMonkey.Api.Infrastructures.MongoDb;
using MongoDB.Driver;

namespace IronMonkey.Api.Repository;

public class RecordRepository : IRecordRepository {
    private readonly IMongoCollection<Record> _collection;
    public RecordRepository(IMongoDbContext context) {
        var db = context.Client.GetDatabase(context.DatabaseName);
        this._collection = db.GetCollection<Record>("Records");
    }

    public async void Create(Record record) {

        var existing = await this.GetByName(record.Name);
        if(existing == null) {
            await _collection.InsertOneAsync(record);
        }
    }

    public async Task<Record?> GetByName(string name) {
        Console.WriteLine("finding Record "+ name);
        var f = await _collection.FindAsync(x => x.Name == name);
        
        return await f.FirstAsync();
    }
}