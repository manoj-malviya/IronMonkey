using Microsoft.EntityFrameworkCore;
using IronMonkey.Api.Repository;
using IronMonkey.Api.Contracts;
using IronMonkey.Api.Infrastructures.Tenants;

namespace IronMonkey.Api.Extensions
{
    public static class DataExtensions
    {
        public static WebApplication ApplyMigrations(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

            var tenants = app.Configuration.GetSection<TenantConfigurationSection>();

            foreach(var t in tenants)
            {
                var connectionString = t.ConnectionString;
                db.Database.SetConnectionString(connectionString);
                if (db.Database.GetMigrations().Count() > 0)
                {
                    db.Database.Migrate();
                }
            }
            return app;
        }
    }
}