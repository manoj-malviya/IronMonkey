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

        // Register factory for creating per-tenant DbContexts on demand.
        // Interceptors registered as IInterceptor are attached to every context the factory
        // creates — see TenantDbContextFactory. Without this the activity interceptor is
        // resolvable but never attached, which is how it silently logged nothing.
        services.AddSingleton<ITenantDbContextFactory>(sp =>
            new TenantDbContextFactory(sp.GetServices<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>()));
    }
}
