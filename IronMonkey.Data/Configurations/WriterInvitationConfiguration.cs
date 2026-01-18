using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Types;

namespace IronMonkey.Data.Configurations;

internal sealed class WriterInvitationConfiguration : IEntityTypeConfiguration<WriterInvitation>
{
    public void Configure(EntityTypeBuilder<WriterInvitation> builder)
    {
        builder.ToTable("writer_invitations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(x => x.Message)
            .HasMaxLength(1000);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => new { x.Email, x.PublisherId })
            .IsUnique();

        builder.HasOne(x => x.Publisher)
            .WithMany()
            .HasForeignKey(x => x.PublisherId);
    }
} 