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

        builder.Property(c => c.CustomFields)
            .HasColumnName("custom_field_values")
            .HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<CustomFieldValues>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new());

        builder.HasIndex(contact => contact.TenantId);

        builder.HasIndex(contact => new { contact.TenantId, contact.Email });
    }
}
