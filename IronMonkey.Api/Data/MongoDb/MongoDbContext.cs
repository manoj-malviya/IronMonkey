using IronMonkey.Api.Domain.Forms.Definitions;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;

namespace IronMonkey.Api.Data.MongoDb;

    public class MongoDbContext : DbContext
    {
        public DbSet<FormDefinition> FormDefinitions {get; init;}
        public MongoDbContext(DbContextOptions options)
            : base(options) {

        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
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