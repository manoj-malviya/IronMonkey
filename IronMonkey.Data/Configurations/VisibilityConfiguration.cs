using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("teams");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Description).HasMaxLength(500);

        // Restrict, not cascade: deleting a parent must not silently delete a subtree — the
        // endpoint refuses while children exist.
        builder.HasOne<Team>().WithMany().HasForeignKey(t => t.ParentTeamId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Members).WithOne().HasForeignKey(m => m.TeamId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Members).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(t => new { t.TenantId, t.ManagerUserId });
    }
}

internal sealed class TeamMembershipConfiguration : IEntityTypeConfiguration<TeamMembership>
{
    public void Configure(EntityTypeBuilder<TeamMembership> builder)
    {
        builder.ToTable("team_memberships");
        builder.HasKey(m => new { m.TeamId, m.UserId });
        builder.HasIndex(m => new { m.TenantId, m.UserId });
    }
}

internal sealed class RoleRecordScopeConfiguration : IEntityTypeConfiguration<RoleRecordScope>
{
    public void Configure(EntityTypeBuilder<RoleRecordScope> builder)
    {
        builder.ToTable("role_record_scopes");
        builder.HasKey(s => new { s.RoleId, s.RecordType });
        builder.Property(s => s.RecordType).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Scope).HasConversion<string>().HasMaxLength(10);

        // A deleted role takes its scopes with it.
        builder.HasOne<Role>().WithMany().HasForeignKey(s => s.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}
