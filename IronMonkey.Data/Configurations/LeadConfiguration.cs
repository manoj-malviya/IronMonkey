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

        builder.Property(lead => lead.PipelineId).IsRequired();

        // Restrict: a pipeline must never be removed out from under the records in it.
        builder.HasOne(lead => lead.Pipeline)
            .WithMany()
            .HasForeignKey(lead => lead.PipelineId)
            .OnDelete(DeleteBehavior.Restrict);

        // The list, board and dashboard all filter by pipeline first and then by stage, so
        // the composite is what keeps a pipeline-scoped aggregate an index seek rather than
        // a scan of every leads in the tenant.
        builder.HasIndex(lead => new { lead.TenantId, lead.PipelineId, lead.PipelineStageId })
            .HasDatabaseName("IX_leads_TenantId_PipelineId_PipelineStageId");

        builder.HasIndex(lead => lead.TenantId);

        builder.HasIndex(lead => new { lead.TenantId, lead.Email });

        builder.HasIndex(lead => lead.IsConverted);
    }
}
