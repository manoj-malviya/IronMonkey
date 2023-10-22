using IronMonkey.Api.Infrastructures.Tenants;

namespace IronMonkey.Api.Infrastructures.MongoDb;

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
        return new MongoDbContext(connectionString);
    }
}