using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IronMonkey.Data.Extensions;

public static class ConfigureDatabase
{
    public static void ConfigureDb(this IServiceCollection services, IConfiguration configuration)
    {
        // Central DB — tenant registry, signup requests, platform admin
        services.AddDbContext<CentralDbContext>(options =>
        {
            var conn = configuration.GetConnectionString("CentralDb")
                ?? throw new InvalidOperationException("CentralDb connection string is required.");
            options.UseNpgsql(conn);
        });

        // Compatibility shim — AppDbContext delegates to CentralDbContext options
        // Allows existing code using AppDbContext to continue working during migration
        services.AddScoped<AppDbContext>(sp =>
        {
            var options = sp.GetRequiredService<DbContextOptions<CentralDbContext>>();
#pragma warning disable CS0618
            return new AppDbContext(options);
#pragma warning restore CS0618
        });

        // Register factory for creating per-tenant DbContexts on demand
        services.AddSingleton<ITenantDbContextFactory, TenantDbContextFactory>();
    }
}
