using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class PipelineStageConfiguration : IEntityTypeConfiguration<PipelineStage>
{
    public void Configure(EntityTypeBuilder<PipelineStage> builder)
    {
        builder.ToTable("pipeline_stages");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.Order).IsRequired();

        builder.Property(p => p.StageType).HasConversion<string>().IsRequired();

        // Ordering is rewritten as a set during a reorder, which moves several stages through
        // positions each other still holds. A unique constraint would fail mid-statement on a
        // legal permutation, so uniqueness of Order is enforced by the reorder endpoint
        // writing a complete, conflict-free sequence rather than by the database.
        builder.HasIndex(p => new { p.TenantId, p.Order });

        // Two stages with the same name are indistinguishable on a board column or a stage
        // picker. The unique index is case-insensitive (on lower(name)) and so is declared
        // in raw SQL by the migration — EF cannot express an expression index here.
    }
}
