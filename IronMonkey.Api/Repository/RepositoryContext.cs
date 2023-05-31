using IronMonkey.Api.Entities.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using IronMonkey.Api.Entities.Configurations;
using IronMonkey.Api.Infrastructures.Tenants;
namespace IronMonkey.Api.Repository
{
    public class RepositoryContext : IdentityDbContext<User>
    {
        private ITenantGetter _tenantService;
        private IConfiguration _config;
        public RepositoryContext(DbContextOptions options, ITenantGetter service, IConfiguration config)
        : base(options)
        {
           _tenantService = service;
           _config = config;
        }

        // protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        // {
        //     var connectionString = _config.GetConnectionString("sqlConnection");
        //     var tenant = _tenantService.Tenant;
        //     // for migrations
        //     connectionString = tenant?.ConnectionString ?? connectionString;
        //     System.Console.WriteLine(connectionString);
        //     // multi-tenant databases
        //     System.Console.WriteLine(connectionString);
        //     optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), b => b.MigrationsAssembly("IronMonkey.Api"));
        // }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            //modelBuilder.ApplyConfiguration(new PersonConfiguration());
            //modelBuilder.ApplyConfiguration(new IronMonkeyConfiguration());
            modelBuilder.ApplyConfiguration(new RoleConfiguration());
        }

        // public DbSet<Company>? Companies { get; set; }
        // public DbSet<Employee>? Employees { get; set; }
    }
}