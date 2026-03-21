using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class LeadMergeConfiguration : IEntityTypeConfiguration<LeadMerge>
{
    public void Configure(EntityTypeBuilder<LeadMerge> builder)
    {
        builder.ToTable("lead_merges");

        builder.HasKey(m => m.Id);

        builder.HasOne<Lead>().WithMany().HasForeignKey(m => m.SourceLeadId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lead>().WithMany().HasForeignKey(m => m.TargetLeadId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(m => m.SourceSnapshot).HasColumnType("text");
        builder.Property(m => m.TargetSnapshot).HasColumnType("text");

        builder.HasIndex(m => m.TenantId);
    }
}
