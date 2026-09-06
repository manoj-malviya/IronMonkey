using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");

        builder.HasKey(rolePermission => new { rolePermission.RoleId, rolePermission.PermissionId });

        builder.HasData([
            // SuperAdmin has all permissions
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.UsersRead.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.UsersWrite.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.UsersDelete.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.LeadsRead.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.LeadsWrite.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.LeadsDelete.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.ContactsRead.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.ContactsWrite.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.ContactsDelete.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.OpportunitiesRead.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.OpportunitiesWrite.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.OpportunitiesDelete.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.ReportsRead.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.ReportsWrite.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.SettingsRead.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.SettingsWrite.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.AdminAccess.Id },
            // Admin is the per-tenant administrator: full CRM access, no platform admin or delete rights
            new RolePermission { RoleId = Role.Admin.Id, PermissionId = Permission.UsersRead.Id },
            new RolePermission { RoleId = Role.Admin.Id, PermissionId = Permission.UsersWrite.Id },
            new RolePermission { RoleId = Role.Admin.Id, PermissionId = Permission.LeadsRead.Id },
            new RolePermission { RoleId = Role.Admin.Id, PermissionId = Permission.LeadsWrite.Id },
            new RolePermission { RoleId = Role.Admin.Id, PermissionId = Permission.ContactsRead.Id },
            new RolePermission { RoleId = Role.Admin.Id, PermissionId = Permission.ContactsWrite.Id },
            new RolePermission { RoleId = Role.Admin.Id, PermissionId = Permission.OpportunitiesRead.Id },
            new RolePermission { RoleId = Role.Admin.Id, PermissionId = Permission.OpportunitiesWrite.Id },
            new RolePermission { RoleId = Role.Admin.Id, PermissionId = Permission.ReportsRead.Id },
            new RolePermission { RoleId = Role.Admin.Id, PermissionId = Permission.SettingsRead.Id },
            // Owner has read access
            new RolePermission { RoleId = Role.Owner.Id, PermissionId = Permission.UsersRead.Id }
        ]);
    }
}