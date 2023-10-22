using IronMonkey.Api.Contracts;
using IronMonkey.Api.Entities.Forms.Definitions;
using IronMonkey.Api.Infrastructures.MongoDb;
using MongoDB.Driver;

namespace IronMonkey.Api.Repository;

public class FormDefinitionRepository : IFormDefinitionRepository {
    private readonly IMongoCollection<FormDefinition> _collection;
    public FormDefinitionRepository(IMongoDbContext context) {
        var db = context.Client.GetDatabase(context.DatabaseName);
        this._collection = db.GetCollection<FormDefinition>("FormDefintion");
    }

    public async void Create(FormDefinition form) {

        var existing = await this.GetByName(form.Name);
        if(existing == null) {
            await _collection.InsertOneAsync(form);
        }
    }

    public async Task<FormDefinition> Get(string id) {
        return await _collection.Find(x => x.Id == id).SingleOrDefaultAsync();

    }

    private async Task<FormDefinition?> GetByName(string name) {
        Console.WriteLine("finding Form "+ name);
        var f = await _collection.Find(x => x.Name == name).SingleOrDefaultAsync();
        
        return f;
    }
}