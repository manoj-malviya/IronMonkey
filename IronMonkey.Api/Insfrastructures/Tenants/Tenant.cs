using IronMonkey.Api.Infrastructures.MongoDb;

namespace IronMonkey.Api.Infrastructures.Tenants;
#nullable disable
public class Tenant
{
    public string Name { get; set; }
    public string ConnectionString { get; set; }

    public string MongoConnectionString {get; set;}
    public string MongoDatabaseName {get; set;}
}