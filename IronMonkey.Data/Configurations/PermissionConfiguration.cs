using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");

        builder.HasKey(permission => permission.Id);

        builder.HasData([
            Permission.UsersRead,
            Permission.UsersWrite,
            Permission.UsersDelete,
            Permission.LeadsRead,
            Permission.LeadsWrite,
            Permission.LeadsDelete,
            Permission.ContactsRead,
            Permission.ContactsWrite,
            Permission.ContactsDelete,
            Permission.OpportunitiesRead,
            Permission.OpportunitiesWrite,
            Permission.OpportunitiesDelete,
            Permission.ReportsRead,
            Permission.ReportsWrite,
            Permission.SettingsRead,
            Permission.SettingsWrite,
            Permission.AdminAccess
        ]);
    }
}