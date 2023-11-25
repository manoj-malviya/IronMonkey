using IronMonkey.Api.Infrastructures.Tenants;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;

namespace IronMonkey.Api.Data.MongoDb;

public class MongoDbContextFactory
{
    private readonly ITenantGetter _tenantService;
    private readonly IConfiguration _config;

    public MongoDbContextFactory(ITenantGetter tenantService, IConfiguration configuration)
    {
        _tenantService = tenantService;
        _config = configuration;
    }

    public MongoDbContext CreateMongoDbContext()
    {
        var defaultString = _config.GetConnectionString("MongoConnectionString");
        var tenant = _tenantService.Tenant;

        var connectionString = tenant?.MongoConnectionString ?? defaultString; // Replace with your logic to retrieve the connection string
        ArgumentNullException.ThrowIfNullOrEmpty(connectionString);

        var databaseName = tenant?.MongoDatabaseName ?? "iron-monkey";

        var mongoDb = new MongoClient(connectionString).GetDatabase(databaseName);
        var dbContextOptions = new DbContextOptionsBuilder<MongoDbContext>()
            .UseMongoDB(mongoDb.Client, mongoDb.DatabaseNamespace.DatabaseName);
            
        return new MongoDbContext(dbContextOptions.Options);
    }
}