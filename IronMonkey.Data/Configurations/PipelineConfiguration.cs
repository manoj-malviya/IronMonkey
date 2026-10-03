using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class PipelineConfiguration : IEntityTypeConfiguration<Pipeline>
{
    public void Configure(EntityTypeBuilder<Pipeline> builder)
    {
        builder.ToTable("pipelines");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.RecordType).HasConversion<string>().IsRequired();

        // The picker query is "this tenant's pipelines of this record type, in order".
        builder.HasIndex(p => new { p.TenantId, p.RecordType, p.Order });

        // Pipeline names are unique per tenant and record type, case-insensitively — two
        // pipelines a user cannot tell apart in a picker are a configuration mistake, not a
        // feature. Declared in raw SQL by the migration for the same reason the stage name
        // index is: EF cannot express an index over lower(Name).
        //
        // The single-default invariant is likewise a filtered unique index declared in SQL:
        //   UNIQUE (TenantId, RecordType) WHERE "IsDefault" AND NOT "IsDeleted"
        // EF cannot express a partial index either, and this one carries real weight — it is
        // what makes "no pipeline specified" resolve to exactly one answer, which is the
        // whole basis of leaving a single-pipeline tenant's UX untouched.
    }
}
