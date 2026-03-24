using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class StageTransitionEntityConfiguration : IEntityTypeConfiguration<StageTransition>
{
    public void Configure(EntityTypeBuilder<StageTransition> builder)
    {
        builder.ToTable("stage_transitions");
        builder.HasKey(t => t.Id);
        builder.HasOne(t => t.FromStage).WithMany().HasForeignKey(t => t.FromStageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.ToStage).WithMany().HasForeignKey(t => t.ToStageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => new { t.TenantId, t.FromStageId, t.ToStageId }).IsUnique();
    }
}
