using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class UserAuditLogConfiguration : IEntityTypeConfiguration<UserAuditLog>
{
    public void Configure(EntityTypeBuilder<UserAuditLog> builder)
    {
        builder.ToTable("user_audit_logs");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.EventType).HasMaxLength(64).IsRequired();
        builder.Property(a => a.TargetEmail).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Detail).HasMaxLength(500);

        // The audit view is "what happened to this user, newest first"; and the unfiltered
        // trail is read in the same order.
        builder.HasIndex(a => new { a.TenantId, a.TargetUserId, a.OccurredAt })
            .HasDatabaseName("IX_UserAuditLogs_TenantId_TargetUserId_OccurredAt");

        // No FK to User: the trail must survive a hard delete of the row it describes, and
        // an invitation event has no user at all until it is accepted.
    }
}
