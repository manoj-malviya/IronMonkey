using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class SavedViewConfiguration : IEntityTypeConfiguration<SavedView>
{
    public void Configure(EntityTypeBuilder<SavedView> builder)
    {
        builder.ToTable("saved_views");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Name).IsRequired().HasMaxLength(100);
        builder.Property(v => v.RecordType).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.DefinitionJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(v => new { v.TenantId, v.RecordType, v.OwnerUserId });
    }
}

internal sealed class UserDefaultViewConfiguration : IEntityTypeConfiguration<UserDefaultView>
{
    public void Configure(EntityTypeBuilder<UserDefaultView> builder)
    {
        builder.ToTable("user_default_views");
        builder.HasKey(d => new { d.UserId, d.RecordType });
        builder.Property(d => d.RecordType).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<SavedView>().WithMany().HasForeignKey(d => d.SavedViewId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReportDefinitionConfiguration : IEntityTypeConfiguration<ReportDefinition>
{
    public void Configure(EntityTypeBuilder<ReportDefinition> builder)
    {
        builder.ToTable("report_definitions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(100);
        builder.Property(r => r.RecordType).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Schedule).HasConversion<string>().HasMaxLength(10);
        builder.Property(r => r.DefinitionJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(r => new { r.TenantId, r.OwnerUserId });
    }
}

internal sealed class ReportRunConfiguration : IEntityTypeConfiguration<ReportRun>
{
    public void Configure(EntityTypeBuilder<ReportRun> builder)
    {
        builder.ToTable("report_runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ResultJson).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.Error).HasMaxLength(1000);
        builder.HasOne<ReportDefinition>().WithMany().HasForeignKey(r => r.ReportDefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.TenantId, r.ReportDefinitionId, r.RanAt });
    }
}

internal sealed class ExportJobConfiguration : IEntityTypeConfiguration<ExportJob>
{
    public void Configure(EntityTypeBuilder<ExportJob> builder)
    {
        builder.ToTable("export_jobs");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.RecordType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Source).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(300).IsRequired();
        builder.Property(e => e.DefinitionJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.FileName).HasMaxLength(200);
        builder.Property(e => e.Error).HasMaxLength(1000);
        builder.HasIndex(e => new { e.TenantId, e.RequestedByUserId, e.CreatedAt });
    }
}
