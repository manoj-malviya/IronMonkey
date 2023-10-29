using Microsoft.EntityFrameworkCore;
using IronMonkey.Api.Repository;
using IronMonkey.Api.Contracts;
using IronMonkey.Api.Infrastructures.Tenants;
using IronMonkey.Api.Infrastructures.MongoDb;
using IronMonkey.Api.Data.MongoDb;

namespace IronMonkey.Api.Extensions
{
    public static class DataExtensions
    {
        public static WebApplication ApplyMigrations(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MongoDbContext>();

            var section = app.Configuration.Get<TenantConfigurationSection>();

            // foreach(var t in section.Tenants)
            // {
            //     var connectionString = t.ConnectionString;
            //     System.Console.WriteLine(connectionString);
            //     db.Database.SetConnectionString(connectionString);
            //     if (db.Database.GetMigrations().Count() > 0)
            //     {
            //         db.Database.Migrate();
            //     }
            // }
            return app;
        }
    }
}