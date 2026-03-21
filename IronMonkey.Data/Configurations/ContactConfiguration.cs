using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts");

        builder.HasKey(contact => contact.Id);

        builder.Property(contact => contact.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(contact => contact.Mobile)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(contact => contact.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(contact => contact.TenantId);

        builder.HasIndex(contact => new { contact.TenantId, contact.Email });
    }
}
