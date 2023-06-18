using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using IronMonkey.Api.Repository;
using IronMonkey.Api.Contracts;
using IronMonkey.Api.Infrastructures.Tenants;
using IronMonkey.Api.Infrastructures.Workflows;
using System.Net.Http.Headers;
using IronMonkey.Api.Infrastructures.MongoDb;
using Microsoft.AspNetCore.Identity;
using AspNetCore.Identity.Mongo;
using IronMonkey.Api.Entities.Models;
using Microsoft.Extensions.DependencyInjection;

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

        // public static void ConfigureSqlContext(this IServiceCollection services, IConfiguration configuration)
        // {
        //     var connectionString = configuration.GetConnectionString("sqlConnection");
        //     services.AddDbContext<RepositoryContext>((s, opts) =>
        //     {

        //         var tenant = s.GetService<ITenantGetter>()?.Tenant;
        //         // for migrations
        //         connectionString = tenant?.ConnectionString ?? connectionString;
        //         // multi-tenant databases
        //         System.Console.WriteLine(connectionString);

        //         opts.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), b => b.MigrationsAssembly("IronMonkey.Api"));
        //     });
        // }

        public static void ConfigureMongoContext(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<MongoDbContextFactory>();
            services.AddScoped<IMongoDbContext>(provider =>
            {
                var factory = provider.GetRequiredService<MongoDbContextFactory>();
                return factory.CreateMongoDbContext();
            });
        }

        public static void ConfigureMongoIdentity(this IServiceCollection services)
        {
            services.AddScoped<MongoDbIdentityOptionFactory>();
            var svc = services.BuildServiceProvider();
            var factory = svc.GetRequiredService<MongoDbIdentityOptionFactory>();
            services.AddIdentityMongoDbProvider<ApplicationUser>(identity => {
                    //identity.Password.RequiredLength = 7;
                },
                mongo => {
                    var config = factory.CreateMongoIdentityOption();
                    System.Console.WriteLine(config.ConnectionString);
                    mongo.ConnectionString = config.ConnectionString; //"mongodb://user:pass@localhost:27018/default-db?authSource=admin";
                }
            ).AddDefaultTokenProviders();
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

        public static void ConfigureElsaClient(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddHttpClient<ElsaClient>(httpClient =>
            {
                var url = configuration["Elsa:ServerUrl"]!.TrimEnd('/') + '/';
                var apiKey = configuration["Elsa:ApiKey"]!;
                
                httpClient.BaseAddress = new Uri(url);
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", apiKey);
            });
        }

    }

}