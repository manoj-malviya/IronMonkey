using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Workflow;

namespace IronMonkey.Data.Configurations;

internal sealed class WorkflowExecutionStepConfiguration : IEntityTypeConfiguration<WorkflowExecutionStep>
{
    public void Configure(EntityTypeBuilder<WorkflowExecutionStep> builder)
    {
        builder.ToTable("workflow_execution_steps");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Kind).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(s => s.ErrorCategory).HasConversion<string>().HasMaxLength(40).IsRequired();

        // A snapshot of whatever the rule declared, including an unsupported value — so it is
        // a sized string, not an enum.
        builder.Property(s => s.ActionType).HasMaxLength(60);
        builder.Property(s => s.Message).HasMaxLength(WorkflowDiagnosticRedactor.MaxMessageLength + 1);
        builder.Property(s => s.TargetHost).HasMaxLength(300);
        builder.Property(s => s.RecipientRedacted).HasMaxLength(320);

        // Steps are only ever read as one execution's ordered timeline.
        builder.HasIndex(s => new { s.WorkflowExecutionLogId, s.Sequence })
            .HasDatabaseName("IX_WorkflowExecutionSteps_LogId_Sequence");

        // Filtering the list by action type ("show me the webhook runs") resolves through
        // the steps, so that path needs its own index.
        builder.HasIndex(s => new { s.TenantId, s.ActionType })
            .HasDatabaseName("IX_WorkflowExecutionSteps_TenantId_ActionType");
    }
}
