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

        builder.Property(opportunity => opportunity.Stage)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(opportunity => opportunity.ExpectedCloseDate)
            .IsRequired();

        builder.Property(opportunity => opportunity.LossReason)
            .HasMaxLength(500);

        builder.HasIndex(opportunity => opportunity.TenantId);

        builder.HasIndex(opportunity => opportunity.ContactId);

        builder.HasIndex(opportunity => opportunity.Stage);

        builder.HasOne(opportunity => opportunity.Contact)
            .WithMany()
            .HasForeignKey(opportunity => opportunity.ContactId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
