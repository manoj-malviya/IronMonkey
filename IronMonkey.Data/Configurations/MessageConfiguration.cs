using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Workflow;

namespace IronMonkey.Data.Configurations;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");
        builder.HasKey(m => m.Id);

        // Enums as strings, for the same reason the workflow history stores them that way: a
        // reordered enum member cannot silently reinterpret existing rows, and support work
        // reads these in psql.
        builder.Property(m => m.Channel).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.Direction).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.ErrorCategory).HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(m => m.Provider).HasMaxLength(50).IsRequired();
        builder.Property(m => m.ProviderMessageId).HasMaxLength(200);
        builder.Property(m => m.FromAddress).HasMaxLength(320).IsRequired();
        builder.Property(m => m.ToAddress).HasMaxLength(320).IsRequired();
        builder.Property(m => m.Subject).HasMaxLength(500);
        builder.Property(m => m.Body).IsRequired();

        // Sized to the redactor's cap: the column is the last line of defence against an
        // unredacted provider error being persisted as tenant-readable history.
        builder.Property(m => m.ErrorMessage).HasMaxLength(WorkflowDiagnosticRedactor.MaxMessageLength + 1);

        builder.Property(m => m.IdempotencyKey).HasMaxLength(200).IsRequired();

        // The idempotency guarantee, enforced by the database rather than by a read-then-write
        // in application code. Two workers racing the same logical send both try to insert;
        // one wins and the other gets a unique violation instead of delivering a duplicate.
        // Scoped by tenant so two tenants cannot collide on a key either of them chose.
        builder.HasIndex(m => new { m.TenantId, m.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("IX_Messages_TenantId_IdempotencyKey");

        // Delivery receipts arrive keyed by the provider's id, and that lookup is on the hot
        // path of every webhook.
        builder.HasIndex(m => new { m.TenantId, m.ProviderMessageId })
            .HasDatabaseName("IX_Messages_TenantId_ProviderMessageId");

        // The lead and contact timelines: (tenant, record) ordered by time.
        builder.HasIndex(m => new { m.TenantId, m.LeadId, m.QueuedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_Messages_TenantId_LeadId_QueuedAt");

        builder.HasIndex(m => new { m.TenantId, m.ContactId, m.QueuedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_Messages_TenantId_ContactId_QueuedAt");

        // Matching an inbound reply back to a record, and finding a customer's history across
        // both directions.
        builder.HasIndex(m => new { m.TenantId, m.Channel, m.ToAddress })
            .HasDatabaseName("IX_Messages_TenantId_Channel_ToAddress");

        builder.HasIndex(m => new { m.TenantId, m.Channel, m.FromAddress })
            .HasDatabaseName("IX_Messages_TenantId_Channel_FromAddress");

        // No FK to Lead or Contact: a message is a historical record of a conversation that
        // happened, and must survive the lead being deleted or merged away. A cascade here
        // would erase the evidence of what was said to a customer.
    }
}
