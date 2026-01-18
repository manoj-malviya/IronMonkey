using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Types;

namespace IronMonkey.Data.Configurations;

internal sealed class WriterConnectionConfiguration : IEntityTypeConfiguration<WriterConnection>
{
    public void Configure(EntityTypeBuilder<WriterConnection> builder)
    {
        builder.ToTable("writer_connections");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => new { x.WriterId, x.PublisherId })
            .IsUnique();
    }
} 