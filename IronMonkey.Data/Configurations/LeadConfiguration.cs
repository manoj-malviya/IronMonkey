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

        builder.Property(lead => lead.Source)
            .HasConversion<string>()
            .HasDefaultValue(LeadSource.Manual);

        builder.Property(lead => lead.IsConverted)
            .IsRequired();

        builder.Property(l => l.CustomFields)
            .HasColumnName("custom_field_values")
            .HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<CustomFieldValues>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new());

        builder.HasOne(l => l.Stage)
            .WithMany()
            .HasForeignKey(l => l.PipelineStageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(lead => lead.TenantId);

        builder.HasIndex(lead => new { lead.TenantId, lead.Email });

        builder.HasIndex(lead => lead.IsConverted);
    }
}
