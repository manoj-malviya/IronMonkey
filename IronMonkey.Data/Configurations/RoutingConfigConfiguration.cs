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
    
        // Nullable pipeline target: null is tenant-wide. Deliberately NOT a foreign key.
        //
        // An FK here would force a choice between Restrict (a pipeline could not be removed
        // until every rule targeting it was hand-edited) and SetNull (the database would
        // silently widen a narrowly-targeted routing config to the whole tenant the moment a
        // pipeline went away). Reverting to tenant-wide IS the chosen behaviour, but it is a
        // product decision the delete endpoint makes explicitly, reports in its impact
        // response and states in its result message — not something a cascade rule does
        // behind the Admin's back.
        builder.HasIndex(config => new { config.TenantId, config.PipelineId });
    }
}
