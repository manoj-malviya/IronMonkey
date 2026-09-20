using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class TeamInvitationConfiguration : IEntityTypeConfiguration<TeamInvitation>
{
    public void Configure(EntityTypeBuilder<TeamInvitation> builder)
    {
        builder.ToTable("team_invitations");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Email).HasMaxLength(256).IsRequired();
        builder.Property(i => i.Name).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Message).HasMaxLength(1000);
        builder.Property(i => i.TokenHash).HasMaxLength(200).IsRequired();
        builder.Property(i => i.TokenPrefix).HasMaxLength(16).IsRequired();
        builder.Property(i => i.FailureReason).HasMaxLength(500);
        builder.Property(i => i.Status).HasConversion<int>();

        // The lookup on the acceptance path: find candidate rows by prefix, then verify the
        // full token against the BCrypt hash. Without this index every acceptance scans the
        // table. Scoped by tenant because every query here is.
        builder.HasIndex(i => new { i.TenantId, i.TokenPrefix })
            .HasDatabaseName("IX_TeamInvitations_TenantId_TokenPrefix");

        // Drives the list page's status filter and the duplicate-invite check.
        builder.HasIndex(i => new { i.TenantId, i.Email })
            .HasDatabaseName("IX_TeamInvitations_TenantId_Email");
    }
}
