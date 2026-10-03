using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class WorkflowRuleConfiguration : IEntityTypeConfiguration<WorkflowRule>
{
    public void Configure(EntityTypeBuilder<WorkflowRule> builder)
    {
        builder.ToTable("workflow_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Trigger).HasConversion<string>().IsRequired();
        builder.Property(r => r.ConditionJson).IsRequired().HasColumnType("jsonb");
        builder.Property(r => r.ActionJson).IsRequired().HasColumnType("jsonb");
        builder.HasIndex(r => new { r.TenantId, r.IsActive });
    
        // Nullable pipeline target: null is tenant-wide. Deliberately NOT a foreign key.
        //
        // An FK here would force a choice between Restrict (a pipeline could not be removed
        // until every rule targeting it was hand-edited) and SetNull (the database would
        // silently widen a narrowly-targeted rule to the whole tenant the moment a
        // pipeline went away). Reverting to tenant-wide IS the chosen behaviour, but it is a
        // product decision the delete endpoint makes explicitly, reports in its impact
        // response and states in its result message — not something a cascade rule does
        // behind the Admin's back.
        builder.HasIndex(rule => new { rule.TenantId, rule.PipelineId });
    }
}
