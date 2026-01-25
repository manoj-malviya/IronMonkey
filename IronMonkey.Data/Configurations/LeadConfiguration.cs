using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> builder)
    {
        builder.ToTable("leads");

        builder.HasKey(lead => lead.Id);

        builder.Property(lead => lead.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(lead => lead.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(lead => lead.Mobile)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(lead => lead.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(lead => lead.LeadSource)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(lead => lead.IsConverted)
            .IsRequired();

        builder.HasIndex(lead => lead.TenantId);

        builder.HasIndex(lead => new { lead.TenantId, lead.Email });

        builder.HasIndex(lead => lead.IsConverted);
    }
}
