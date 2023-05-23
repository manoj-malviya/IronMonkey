using IronMonkey.Api.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using IronMonkey.Api.Entities.Configurations;

namespace IronMonkey.Api.Repository
{
    public class RepositoryContext : IdentityDbContext<User>
    {
        public RepositoryContext(DbContextOptions options)
        : base(options)
        {
        }

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