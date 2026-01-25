using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(tenant => tenant.Id);

        builder.Property(tenant => tenant.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(tenant => tenant.Slug)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(tenant => tenant.Slug)
            .IsUnique();

        builder.Property(tenant => tenant.SubscriptionPlan)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(tenant => tenant.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(tenant => tenant.DatabaseConnectionString)
            .HasMaxLength(500);

        builder.Property(tenant => tenant.ThemeSettings)
            .HasColumnType("jsonb");
    }
}
