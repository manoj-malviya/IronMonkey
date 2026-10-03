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

        builder.Property(c => c.FieldKey)
            .IsRequired()
            .HasMaxLength(100)
            .HasDefaultValue(string.Empty);

        builder.Property(c => c.FieldType)
            .HasConversion<string>();

        builder.Property(c => c.Options).HasConversion(
            v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
            v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new());

        builder.Property(c => c.HelpText).HasMaxLength(500);

        builder.Property(c => c.DefaultValue).HasMaxLength(500);

        builder.Property(c => c.IsArchived).HasDefaultValue(false);

        builder.Property(c => c.AppliesTo)
            .HasConversion<string>()
            .HasDefaultValue(CustomFieldEntity.Lead);

        builder.Property(c => c.DisplayOrder)
            .HasDefaultValue(0);

        builder.HasIndex(c => c.TenantId);

        // Forms fetch every field for one record type, ordered — this serves that directly.
        builder.HasIndex(c => new { c.TenantId, c.AppliesTo, c.DisplayOrder });

        // The internal key is what integrations address, so it must resolve to exactly one
        // field within a scope. Archived fields still hold it — their values are still stored.
        builder.HasIndex(c => new { c.TenantId, c.AppliesTo, c.FieldKey })
            .IsUnique()
            .HasDatabaseName("ix_custom_field_definitions_tenant_scope_key_unique");
    
        // Nullable pipeline target: null is tenant-wide. Deliberately NOT a foreign key.
        //
        // An FK here would force a choice between Restrict (a pipeline could not be removed
        // until every rule targeting it was hand-edited) and SetNull (the database would
        // silently widen a narrowly-targeted field to the whole tenant the moment a
        // pipeline went away). Reverting to tenant-wide IS the chosen behaviour, but it is a
        // product decision the delete endpoint makes explicitly, reports in its impact
        // response and states in its result message — not something a cascade rule does
        // behind the Admin's back.
        builder.HasIndex(field => new { field.TenantId, field.PipelineId });
    }
}
