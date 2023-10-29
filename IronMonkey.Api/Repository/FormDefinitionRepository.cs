using System.Linq;
using IronMonkey.Api.Contracts;
using IronMonkey.Api.Core;
using IronMonkey.Api.Data.MongoDb;
using IronMonkey.Api.Domain.Forms;
using IronMonkey.Api.Domain.Forms.Definitions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;

namespace IronMonkey.Api.Repository;

public class FormDefinitionRepository : IFormDefinitionRepository {
    private readonly MongoDbContext db;
    public FormDefinitionRepository(MongoDbContext context) {
        this.db = context;
    }

    public async void Create(FormDefinition form) {

        var existing = await this.GetByName(form.Name);
        if(existing == null) {
            await db.FormDefinitions.AddAsync(form);
        } else {
            var doc = await db.FormDefinitions.FirstOrDefaultAsync(d => d.Id == form.Id);

            doc.Collection = form.Collection;
            doc.Fields = form.Fields;
            doc.Name = form.Name;
        }

        await db.SaveChangesAsync();
    }

    public async Task<FormDefinition?> Get(string id) {
        return await db.FormDefinitions.FirstOrDefaultAsync(x => x.Id.Equals(id));
    }

    private async Task<FormDefinition?> GetByName(string name) {
        Console.WriteLine("finding Form "+ name);
        var f = await db.FormDefinitions.FirstOrDefaultAsync(x => x.Name == name);
        return f;
    }

    public async Task<PagedList<FormDefinitionRow>> GetList(int page, int pageSize) {
        // var filter = Builders<FormDefinition>.Filter.Empty;
        // var projection = Builders<FormDefinition>.Projection
        //     .Include(doc => doc.Name)
        //     .Include(doc => doc.Id);
        // var options = new FindOptions<FormDefinition>
        // {
        //     Projection = projection,
        //     Skip = (page - 1) * pageSize, // Calculate how many documents to skip
        //     Limit = pageSize // Limit the number of documents per page
        // };

        // var query = await _collection.FindAsync(filter, options);
        var result = db.FormDefinitions.Select( r => 
            new FormDefinitionRow(){
                Id = r.Id,
                Name = r.Name
            }
        ).AsQueryable();
        return PagedList<FormDefinitionRow>.ToPagedList(result, page, pageSize);
    }
}