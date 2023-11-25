using IronMonkey.Api.Domain.Forms.Definitions;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using MongoDB.EntityFrameworkCore.Extensions;

namespace IronMonkey.Api.Data.MongoDb;

    public class MongoDbContext : DbContext
    {
        public DbSet<FormDefinitionEntity> FormDefinitions {get; init;}
        public DbSet<FormDefinitionFieldEntity> Fields {get; set;}
        public MongoDbContext(DbContextOptions options)
            : base(options) {

        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<FormDefinitionEntity>().ToCollection("FormDefinitions");
            modelBuilder.Entity<FormDefinitionFieldEntity>().ToCollection("FormDefinitionFields");
        }

        // public MongoDbContext(string connectionString, string databaseName) {
        //     this.ConnectionString = connectionString;
        //     this.DatabaseName = databaseName;
        //     Console.WriteLine(this.ConnectionString);
        //     this.Client = new MongoClient(connectionString);
        // }
        // public string ConnectionString {get; set;}
        // public string DatabaseName {get; set;}

        // public MongoClient Client {get;}
    }