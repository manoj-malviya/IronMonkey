using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class StageChangeConfiguration : IEntityTypeConfiguration<StageChange>
{
    public void Configure(EntityTypeBuilder<StageChange> builder)
    {
        builder.ToTable("stage_changes");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.RecordType).HasConversion<string>().IsRequired();
        builder.Property(c => c.RecordId).IsRequired();
        builder.Property(c => c.ToStageId).IsRequired();
        builder.Property(c => c.OccurredAt).IsRequired();
        builder.Property(c => c.ToPipelineId).IsRequired();

        // No foreign key to pipelines, for the same reason RecordId has none: history must
        // survive a pipeline being removed. What pipeline a record was in last March is a
        // fact about the past that deleting configuration today must not erase.

        // Restrict, not Cascade: deleting a stage must not silently erase the history of
        // every record that ever passed through it. The stage endpoints deactivate rather
        // than delete anyway, so this only ever fires on a genuine hard delete.
        builder.HasOne(c => c.FromStage)
            .WithMany()
            .HasForeignKey(c => c.FromStageId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.ToStage)
            .WithMany()
            .HasForeignKey(c => c.ToStageId)
            .OnDelete(DeleteBehavior.Restrict);

        // The reporting query is "every move of this record, in order" — time in a stage is
        // the gap between consecutive rows, so the sort key belongs in the index.
        builder.HasIndex(c => new { c.TenantId, c.RecordType, c.RecordId, c.OccurredAt })
            .HasDatabaseName("IX_stage_changes_TenantId_Record_OccurredAt");

        // Velocity across the tenant: how long records sat in a given stage over a window.
        builder.HasIndex(c => new { c.TenantId, c.ToStageId, c.OccurredAt })
            .HasDatabaseName("IX_stage_changes_TenantId_ToStageId_OccurredAt");

        // Velocity within one pipeline: the same question as above, scoped so one pipeline's
        // history is never summed into another's.
        builder.HasIndex(c => new { c.TenantId, c.ToPipelineId, c.OccurredAt })
            .HasDatabaseName("IX_stage_changes_TenantId_ToPipelineId_OccurredAt");

        // Deliberately NOT unique on (record, from, to): the same record legitimately moves
        // back and forth between two stages, and each of those moves is a separate event.
        // This is the constraint that makes StageTransition unusable as a history table.
    }
}
