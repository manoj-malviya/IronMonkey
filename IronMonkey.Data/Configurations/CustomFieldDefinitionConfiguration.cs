using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class CustomFieldDefinitionConfiguration : IEntityTypeConfiguration<CustomFieldDefinition>
{
    public void Configure(EntityTypeBuilder<CustomFieldDefinition> builder)
    {
        builder.ToTable("custom_field_definitions");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).IsRequired();

        builder.Property(c => c.FieldName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.FieldType)
            .HasConversion<string>();

        builder.Property(c => c.Options).HasConversion(
            v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
            v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new());

        builder.HasIndex(c => c.TenantId);
    }
}
