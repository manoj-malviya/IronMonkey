using AspNetCore.Identity.Mongo;
using IronMonkey.Api.Data.MongoDb;
using IronMonkey.Api.Infrastructures.Tenants;

namespace IronMonkey.Api.Infrastructures.MongoDb;

public class MongoDbIdentityOptionFactory
{
    private readonly ITenantGetter _tenantService;
    private readonly IConfiguration _config;

    public MongoDbIdentityOptionFactory(ITenantGetter tenantService, IConfiguration configuration)
    {
        _tenantService = tenantService;
        _config = configuration;
    }

    public MongoIdentityOptions CreateMongoIdentityOption()
    {
        var opt = new MongoIdentityOptions();
        var defaultString = _config.GetConnectionString("MongoConnectionString");
        var tenant = _tenantService.Tenant;

        var connectionString = tenant?.MongoConnectionString ?? defaultString; // Replace with your logic to retrieve the connection string
        ArgumentNullException.ThrowIfNullOrEmpty(connectionString);

        opt.ConnectionString = connectionString;
        return opt;
    }
}