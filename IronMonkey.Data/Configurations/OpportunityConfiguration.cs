using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class OpportunityConfiguration : IEntityTypeConfiguration<Opportunity>
{
    public void Configure(EntityTypeBuilder<Opportunity> builder)
    {
        builder.ToTable("opportunities");

        builder.HasKey(opportunity => opportunity.Id);

        builder.Property(opportunity => opportunity.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(opportunity => opportunity.PipelineStageId)
            .IsRequired();

        builder.Property(opportunity => opportunity.ExpectedCloseDate)
            .IsRequired();

        builder.Property(opportunity => opportunity.LossReason)
            .HasMaxLength(500);

        builder.Property(opportunity => opportunity.PipelineId).IsRequired();

        // Restrict: a pipeline must never be removed out from under the records in it.
        builder.HasOne(opportunity => opportunity.Pipeline)
            .WithMany()
            .HasForeignKey(opportunity => opportunity.PipelineId)
            .OnDelete(DeleteBehavior.Restrict);

        // The list, board and dashboard all filter by pipeline first and then by stage, so
        // the composite is what keeps a pipeline-scoped aggregate an index seek rather than
        // a scan of every opportunities in the tenant.
        builder.HasIndex(opportunity => new { opportunity.TenantId, opportunity.PipelineId, opportunity.PipelineStageId })
            .HasDatabaseName("IX_opportunities_TenantId_PipelineId_PipelineStageId");

        builder.HasIndex(opportunity => opportunity.TenantId);

        builder.HasIndex(opportunity => opportunity.ContactId);

        // The dashboard groups by stage within a tenant, and the list page filters by it.
        builder.HasIndex(opportunity => new { opportunity.TenantId, opportunity.PipelineStageId })
            .HasDatabaseName("IX_opportunities_TenantId_PipelineStageId");

        builder.HasOne(opportunity => opportunity.Contact)
            .WithMany()
            .HasForeignKey(opportunity => opportunity.ContactId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict rather than Cascade: an opportunity must never be deleted as a side
        // effect of removing a stage. The delete endpoint reassigns first and deactivates
        // rather than deleting, so this is the backstop for anything reaching the table
        // another way.
        builder.HasOne(opportunity => opportunity.Stage)
            .WithMany()
            .HasForeignKey(opportunity => opportunity.PipelineStageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
