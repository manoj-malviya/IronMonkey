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

        builder.Property(tenant => tenant.ApprovalStatus)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue("Pending");

        builder.Property(tenant => tenant.ApprovalNote)
            .HasMaxLength(1000);

        builder.Property(tenant => tenant.IsProvisioned)
            .HasDefaultValue(false);

        builder.Property(tenant => tenant.ApprovedAt);
        builder.Property(tenant => tenant.ProvisionedAt);
        builder.Property(tenant => tenant.AppliedRecipeId);
        builder.Property(tenant => tenant.AppliedRecipeVersion);
    }
}
