using Microsoft.EntityFrameworkCore;
using IronMonkey.Data.Abstractions;
using IronMonkey.Data.Configurations;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data;

/// <summary>
/// DbContext for the central platform database.
/// Holds tenant registry, signup requests, and platform admin data.
/// No tenant isolation filtering — this is the global admin context.
/// </summary>
public class CentralDbContext(DbContextOptions<CentralDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<SignupRequest> SignupRequests => Set<SignupRequest>();
    public DbSet<UserTenantIndex> UserTenantIndex => Set<UserTenantIndex>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new SignupRequestConfiguration());

        modelBuilder.Entity<UserTenantIndex>(b =>
        {
            b.ToTable("UserTenantIndex");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Email);
            b.Property(x => x.Email).IsRequired().HasMaxLength(320);
        });

        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GenerateTimestamps();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void GenerateTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is Entity && (
                e.State == EntityState.Added ||
                e.State == EntityState.Modified ||
                e.State == EntityState.Deleted));

        foreach (var entry in entries)
        {
            var entity = (Entity)entry.Entity;
            entity.UpdatedAt = DateTime.UtcNow;
            if (entry.State == EntityState.Added) entity.CreatedAt = DateTime.UtcNow;
            if (entry.State == EntityState.Deleted)
            {
                entity.DeletedAt = DateTime.UtcNow;
                entity.IsDeleted = true;
            }
        }
    }
}
