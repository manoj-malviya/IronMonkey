using AspNetCore.Identity.Mongo;
using IronMonkey.Api.Data.MongoDb;

namespace IronMonkey.Api.Infrastructures.MongoDb;

public class MongoDbIdentityOptionFactory
{
    private readonly IMongoDbContext _context;

    public MongoDbIdentityOptionFactory(IMongoDbContext context)
    {
        _context = context;
    }

    public MongoIdentityOptions CreateMongoIdentityOption()
    {
        var opt = new MongoIdentityOptions();
        Console.WriteLine("in Factory => "+ _context.ConnectionString);
        opt.ConnectionString = _context.ConnectionString;
        return opt;
    }
}