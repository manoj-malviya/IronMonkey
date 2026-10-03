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

        builder.Property(p => p.RecordType).HasConversion<string>().IsRequired();

        // Ordering is rewritten as a set during a reorder, which moves several stages through
        // positions each other still holds. A unique constraint would fail mid-statement on a
        // legal permutation, so uniqueness of Order is enforced by the reorder endpoint
        // writing a complete, conflict-free sequence rather than by the database.
        //
        // RecordType leads the index because every stage query filters on it first: lead and
        // opportunity stages share this table and each has its own independent 1..n sequence.
        builder.Property(p => p.PipelineId).IsRequired();

        // Restrict: removing a pipeline must never cascade away the stages that still hold
        // records. The delete endpoint refuses while anything points at the pipeline and
        // offers reassignment first, so this is the backstop for anything reaching the table
        // another way.
        builder.HasOne(p => p.Pipeline)
            .WithMany()
            .HasForeignKey(p => p.PipelineId)
            .OnDelete(DeleteBehavior.Restrict);

        // PipelineId leads now: every stage query is scoped to one pipeline (or explicitly
        // to all of them), and each pipeline owns its own independent 1..n Order sequence.
        builder.HasIndex(p => new { p.TenantId, p.PipelineId, p.Order });

        // Kept for the queries that still ask "every stage of this record type in the
        // tenant" — the cross-pipeline views, which are deliberately reachable.
        builder.HasIndex(p => new { p.TenantId, p.RecordType, p.Order });

        // Two stages with the same name are indistinguishable on a board column or a stage
        // picker. The unique index is case-insensitive (on lower(name)) and so is declared
        // in raw SQL by the migration — EF cannot express an expression index here. It is
        // scoped per record type: a tenant may legitimately have a lead stage and an
        // opportunity stage both named "Qualified".
        //
        // With multiple pipelines it is scoped per PIPELINE rather than per record type: two
        // pipelines are different funnels and both may legitimately have a "Qualified"
        // stage. Scoping it to the record type instead would make the second pipeline
        // unable to reuse any name the first had taken.
    }
}
