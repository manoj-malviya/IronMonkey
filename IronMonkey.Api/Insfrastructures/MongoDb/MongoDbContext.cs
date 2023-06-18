using MongoDB.Driver;

namespace IronMonkey.Api.Infrastructures.MongoDb
{
    public interface IMongoDbContext
    {
        public string ConnectionString {get; set;}
        public string DatabaseName {get; set;}
    }

    public class MongoDbContext : IMongoDbContext
    {
        public MongoDbContext(string connectionString)
            : this(connectionString, (new MongoUrl(connectionString)).DatabaseName){

        }

        public MongoDbContext(string connectionString, string databaseName) {
            this.ConnectionString = connectionString;
            this.DatabaseName = databaseName;
        }
        public string ConnectionString {get; set;}
        public string DatabaseName {get; set;}
    }
}