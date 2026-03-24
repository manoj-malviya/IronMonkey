using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class RoutingConfigConfiguration : IEntityTypeConfiguration<RoutingConfig>
{
    public void Configure(EntityTypeBuilder<RoutingConfig> builder)
    {
        builder.ToTable("routing_configs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Strategy).HasConversion<string>().IsRequired();
        builder.Property(r => r.Dimension).HasConversion<string>().IsRequired();
        builder.Property(r => r.TerritoryMapJson).HasColumnType("jsonb");
        builder.Property(r => r.CustomFieldKey).HasMaxLength(100);
        builder.HasIndex(r => r.TenantId).IsUnique();
    }
}
