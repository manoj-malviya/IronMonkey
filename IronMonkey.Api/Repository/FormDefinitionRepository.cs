using System.Linq;
using IronMonkey.Api.Contracts;
using IronMonkey.Api.Core;
using IronMonkey.Api.Data.MongoDb;
using IronMonkey.Api.Domain;
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

        var entityToSave = this.toFormDefinitionEntity(form);
        var entity = await this.SaveDefinition(entityToSave);

        var fieldsToSave = this.toFields(form);
        await this.SaveFields(entity, fieldsToSave);

        await db.SaveChangesAsync();
    }

    private async Task<FormDefinitionEntity> SaveDefinition(FormDefinitionEntity entity) {
        var existing = await this.GetByName(entity.Name);
        if(existing == null) {
            var t = await db.FormDefinitions.AddAsync(entity);
            await db.SaveChangesAsync();
            existing = t.Entity;
            // db.Entry(entity).State = EntityState.Detached;
            existing =  await this.GetByName(entity.Name);
            // existing.Id = db.Entry(entity).Property(e => e.Id).CurrentValue;
        } else {
            //existing = await db.FormDefinitions.FirstAsync(d => d.Id == entity.Id);
            existing.Storage = entity.Storage;
            existing.Name = entity.Name;
        }
        // await db.SaveChangesAsync();
        return existing!;
    }

    private async Task<IEnumerable<FormDefinitionFieldEntity>> SaveFields(FormDefinitionEntity entity, IEnumerable<FormDefinitionFieldEntity> fields)
    {
        var existingFields = await db.Fields.Where(f => f.DefinitionId == entity.Id).ToListAsync();

        foreach(var f in fields) {
            var found = existingFields.Find(ef => ef.Name == f.Name);
            if(found != null) {
                found.Name = f.Name;
                found.Type = f.Type;
            } else {
                f.DefinitionId = entity.Id;
                await db.Fields.AddAsync(f);
            }
        }

        return existingFields;
    }

    public async Task<FormDefinition?> Get(string id) {
        var entity = await db.FormDefinitions.FirstOrDefaultAsync(x => x.Id.Equals(id));
        var domain = toFormDefinition(entity);

        var fieldEntities = await db.Fields.Where(x => x.DefinitionId == entity.Id).ToArrayAsync();
        var fields = this.toFieldDefinition(fieldEntities);
        domain.FieldList = fields;
        return domain;
    }

    private async Task<FormDefinitionEntity?> GetByName(string name) {
        Console.WriteLine("finding Form "+ name);
        var f = await db.FormDefinitions.FirstOrDefaultAsync(x => x.Name == name);
        return f;
    }

    public async Task<PagedList<FormDefinition>> GetList(int page, int pageSize) {
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
        //var result = db.FormDefinitions.Select();

        var formsQuery = db.FormDefinitions.Select(e => new FormDefinition(){
            Id = e.Id,
            Name = e.Name,
            Storage = e.Storage
        }).AsQueryable();

        // return entities.Select(e => new FormDefinition(){
        //     Id = e.Id,
        //     Name = e.Name,
        //     Storage = e.Storage
        // });
        return PagedList<FormDefinition>.ToPagedList(formsQuery, page, pageSize);
    }

    private FormDefinitionEntity toFormDefinitionEntity(FormDefinition definition) {
        return new FormDefinitionEntity() {
            Id = definition.Id,
            Name = definition.Name,
            Storage = definition.Storage
        };
    }

    private IEnumerable<FormDefinitionFieldEntity> toFields(FormDefinition definition) {
        return definition.FieldList.Select(f => new FormDefinitionFieldEntity(){
            Name = f.Name,
            Type = f.FieldType.ToString()
        });
    }

    private FormDefinition toFormDefinition(FormDefinitionEntity entity) {
        return new FormDefinition() {
            Id = entity.Id,
            Name = entity.Name,
            Storage = entity.Storage
        };
    }

    private IEnumerable<FieldDefinition> toFieldDefinition(IEnumerable<FormDefinitionFieldEntity> entities) {
        return entities.Select(x => new FieldDefinition(
            x.Name,
            (Domain.FieldType) int.Parse(x.Type)
        ));
    }
}