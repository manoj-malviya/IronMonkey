using IronMonkey.Api.Contracts;
using IronMonkey.Api.Core;
using IronMonkey.Api.Data.MongoDb;
using IronMonkey.Api.Domain.Forms.Definitions;
using Microsoft.AspNetCore.Mvc;
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
        } else {
            // Document exists, so update it
            var filter = Builders<FormDefinition>.Filter.Eq(fd => fd.Id, existing.Id);
            form.Id = existing.Id;
            var result = await _collection.FindOneAndReplaceAsync(filter, form);
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

    public async Task<PagedList<FormDefinition>> GetList(int page, int pageSize) {
        var filter = Builders<FormDefinition>.Filter.Empty;
        var projection = Builders<FormDefinition>.Projection
            .Include(doc => doc.Name)
            .Include(doc => doc.Id);
        var options = new FindOptions<FormDefinition>
        {
            Projection = projection,
            Skip = (page - 1) * pageSize, // Calculate how many documents to skip
            Limit = pageSize // Limit the number of documents per page
        };
        var query = await _collection.FindAsync(filter, options);
        return new PagedList<FormDefinition>(query.ToList(), 20, page, pageSize);
    }
}