using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Workflow;

namespace IronMonkey.Data.Configurations;

internal sealed class WorkflowExecutionLogConfiguration : IEntityTypeConfiguration<WorkflowExecutionLog>
{
    public void Configure(EntityTypeBuilder<WorkflowExecutionLog> builder)
    {
        builder.ToTable("workflow_execution_logs");
        builder.HasKey(l => l.Id);

        // Enums are stored as strings so a reordered or inserted enum member cannot silently
        // reinterpret history, and so the rows are readable in psql during support work.
        builder.Property(l => l.Trigger).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(l => l.ErrorCategory).HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(l => l.WorkflowRuleName).IsRequired().HasMaxLength(200);
        builder.Property(l => l.LeadName).IsRequired().HasMaxLength(400);
        builder.Property(l => l.CorrelationId).IsRequired().HasMaxLength(100);
        builder.Property(l => l.JobId).HasMaxLength(100);

        // Sized to the redactor's cap rather than left as unbounded text: the column is the
        // last line of defence against a stack trace being persisted as tenant history.
        builder.Property(l => l.ErrorMessage).HasMaxLength(WorkflowDiagnosticRedactor.MaxMessageLength + 1);
        builder.Property(l => l.SkipReason).HasMaxLength(300);

        builder.HasMany(l => l.Steps)
            .WithOne(s => s.ExecutionLog)
            .HasForeignKey(s => s.WorkflowExecutionLogId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(l => l.Steps).HasField("_steps").UsePropertyAccessMode(PropertyAccessMode.Field);

        // The list's default query is (tenant, newest first), and every filter narrows it.
        // DESC on StartedAt so the index serves the ordering rather than only the predicate.
        builder.HasIndex(l => new { l.TenantId, l.StartedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_WorkflowExecutionLogs_TenantId_StartedAt");

        // "Runs for this rule", which the rules list and the "View runs" action both use.
        builder.HasIndex(l => new { l.TenantId, l.WorkflowRuleId, l.StartedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_WorkflowExecutionLogs_TenantId_RuleId_StartedAt");

        // "Runs that touched this lead."
        builder.HasIndex(l => new { l.TenantId, l.LeadId, l.StartedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_WorkflowExecutionLogs_TenantId_LeadId_StartedAt");

        // "Only the failures" — the reason most Admins open this page at all.
        builder.HasIndex(l => new { l.TenantId, l.Status, l.StartedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_WorkflowExecutionLogs_TenantId_Status_StartedAt");

        // Serves both the retention sweep (delete where StartedAt < cutoff) and the
        // reconciliation sweep (Running rows older than the threshold).
        builder.HasIndex(l => new { l.Status, l.StartedAt })
            .HasDatabaseName("IX_WorkflowExecutionLogs_Status_StartedAt");

        // The idempotency key. A retry of one logical trigger must find the row the previous
        // attempt wrote instead of inserting a second "success" for the same work, and the
        // uniqueness is enforced here rather than only in the recorder so a concurrent worker
        // cannot win a race the application-level check would miss.
        //
        // WorkflowRuleId is part of the key because one trigger evaluates every matching rule
        // and each gets its own row: the correlation id identifies the trigger, not the run.
        // Without the rule in the key the second rule's insert collides with the first's and
        // only one rule's history is ever written.
        builder.HasIndex(l => new { l.TenantId, l.CorrelationId, l.WorkflowRuleId, l.Attempt })
            .IsUnique()
            .HasDatabaseName("UX_WorkflowExecutionLogs_TenantId_Correlation_Rule_Attempt");
    }
}
