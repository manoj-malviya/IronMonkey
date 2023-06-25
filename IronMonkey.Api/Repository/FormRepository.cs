using IronMonkey.Api.Contracts;
using IronMonkey.Api.Entities.Leads.Definitions;
using IronMonkey.Api.Infrastructures.MongoDb;
using MongoDB.Driver;

namespace IronMonkey.Api.Repository;

public class FormRepository : IFormRepository {
    private readonly IMongoCollection<Form> _collection;
    public FormRepository(IMongoDbContext context) {
        var db = context.Client.GetDatabase(context.DatabaseName);
        this._collection = db.GetCollection<Form>("Forms");
    }

    public async void Create(Form form) {

        var existing = await this.GetByName(form.Name);
        if(existing == null) {
            await _collection.InsertOneAsync(form);
        }
    }

    public async Task<Form?> GetByName(string name) {
        Console.WriteLine("finding Form "+ name);
        var f = await _collection.Find(x => x.Name == name).SingleOrDefaultAsync();
        
        return f;
    }
}