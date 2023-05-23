using Microsoft.EntityFrameworkCore;
using IronMonkey.Api.Repository;
using IronMonkey.Api.Contracts;
using IronMonkey.Api.Infrastructures.Tenants;

namespace IronMonkey.Api.Extensions
{
    public static class ServiceExtensions
    {
        public static void ConfigureCors(this IServiceCollection services) =>
            services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy", builder =>
                    builder.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader());
            });

        public static void ConfigureIISIntegration(this IServiceCollection services) =>
            services.Configure<IISOptions>(options =>
            {

            });

        public static void ConfigureSqlContext(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("sqlConnection");
            services.AddDbContext<RepositoryContext>((s, opts) =>
            {

                var tenant = s.GetService<ITenantGetter>()?.Tenant;
                // for migrations
                connectionString = tenant?.ConnectionString ?? connectionString;
                // multi-tenant databases
                System.Console.WriteLine(connectionString);

                opts.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), b => b.MigrationsAssembly("IronMonkey.Api"));
            });
        }

        public static void ConfigureRepositoryManager(this IServiceCollection services) =>
           services.AddScoped<IRepositoryManager, RepositoryManager>();

        public static IServiceCollection AddScopedAs<T>(this IServiceCollection services, IEnumerable<Type> types) where T : class
        {
            // register the type first
            services.AddScoped<T>();

            foreach (var type in types)
            {
                // register a scoped 
                services.AddScoped(type, svc =>
                {
                    var rs = svc.GetRequiredService<T>();
                    return rs;
                });
            }

            return services;
        }

    }

}