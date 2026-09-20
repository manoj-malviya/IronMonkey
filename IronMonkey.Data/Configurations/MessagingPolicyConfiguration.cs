using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class MessagingPolicyConfiguration : IEntityTypeConfiguration<MessagingPolicy>
{
    public void Configure(EntityTypeBuilder<MessagingPolicy> builder)
    {
        builder.ToTable("messaging_policies");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.QuietHoursChannels).HasMaxLength(100).IsRequired();

        // One policy per tenant, enforced by the database. Two rows could disagree, and a
        // send path reading the wrong one would ignore the tenant's quiet hours.
        builder.HasIndex(p => p.TenantId)
            .IsUnique()
            .HasDatabaseName("IX_MessagingPolicies_TenantId");
    }
}
