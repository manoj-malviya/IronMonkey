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
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<WebForm> WebForms => Set<WebForm>();
    public DbSet<IndustryRecipe> IndustryRecipes => Set<IndustryRecipe>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new SignupRequestConfiguration());
        modelBuilder.ApplyConfiguration(new IndustryRecipeConfiguration());

        modelBuilder.Entity<UserTenantIndex>(b =>
        {
            b.ToTable("UserTenantIndex");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Email);
            b.Property(x => x.Email).IsRequired().HasMaxLength(320);
        });

        modelBuilder.Entity<PlatformUser>(b =>
        {
            b.ToTable("PlatformUsers");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Email).IsUnique();
            b.Property(x => x.Name).IsRequired().HasMaxLength(200);
            b.Property(x => x.Email).IsRequired().HasMaxLength(320);
            b.Property(x => x.PasswordHash).IsRequired();
            b.Property(x => x.Role).IsRequired().HasMaxLength(50);
            b.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ApiKey>(b =>
        {
            b.ToTable("ApiKeys");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.TenantId);
            b.Property(x => x.KeyHash).IsRequired().HasMaxLength(100);
            b.Property(x => x.KeyPrefix).IsRequired().HasMaxLength(16);
            b.Property(x => x.IsActive).IsRequired();
        });

        modelBuilder.Entity<WebForm>(b =>
        {
            b.ToTable("WebForms");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.FormToken).IsUnique();
            b.HasIndex(x => x.TenantId);
            b.Property(x => x.FormName).IsRequired().HasMaxLength(200);
            b.Property(x => x.FormToken).IsRequired().HasMaxLength(64);
            b.Property(x => x.FieldNamesJson).IsRequired();
            b.Property(x => x.IsActive).IsRequired();
            b.Property(x => x.PostSubmissionRedirectUrl).HasMaxLength(2000);
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
