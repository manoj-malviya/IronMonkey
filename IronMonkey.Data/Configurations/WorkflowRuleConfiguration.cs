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
    }
}
