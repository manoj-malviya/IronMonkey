using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class LeadTaskConfiguration : IEntityTypeConfiguration<LeadTask>
{
    public void Configure(EntityTypeBuilder<LeadTask> builder)
    {
        builder.ToTable("lead_tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Title).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(2000);
        builder.Property(t => t.Priority).HasConversion<string>().IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().IsRequired();
        builder.HasOne(t => t.Lead).WithMany().HasForeignKey(t => t.LeadId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => new { t.TenantId, t.AssignedToUserId });
        builder.HasIndex(t => new { t.TenantId, t.LeadId });
    }
}
