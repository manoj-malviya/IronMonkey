using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Newtonsoft.Json;
using IronMonkey.Data.Types;

namespace IronMonkey.Data.Configurations;

internal sealed class ContentTemplateConfiguration : IEntityTypeConfiguration<ContentTemplate>
{
    public void Configure(EntityTypeBuilder<ContentTemplate> builder)
    {
        builder.ToTable("content_templates");

        builder.HasKey(ct => ct.Id);

        builder.Property(ct => ct.Parts).HasConversion(
            parts => JsonConvert.SerializeObject(parts),
            parts => JsonConvert.DeserializeObject<List<ContentTemplatePart>>(parts)!
        );
    }
}