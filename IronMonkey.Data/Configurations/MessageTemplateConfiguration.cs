using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> builder)
    {
        builder.ToTable("message_templates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Channel).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Subject).HasMaxLength(500);
        builder.Property(t => t.Body).IsRequired();
        builder.Property(t => t.ProviderTemplateName).HasMaxLength(200);

        builder.HasIndex(t => new { t.TenantId, t.Channel, t.IsActive })
            .HasDatabaseName("IX_MessageTemplates_TenantId_Channel_IsActive");
    }
}
