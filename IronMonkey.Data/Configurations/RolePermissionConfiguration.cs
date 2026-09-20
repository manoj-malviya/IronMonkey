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
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.WorkflowLogsRead.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.MessagesSend.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.MessagesRead.Id },
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
            // Workflow execution history: granted to Admin explicitly rather than folded into
            // settings:read, so the grant can be withdrawn from a role that may configure
            // automation but should not see the leads it ran against.
            new RolePermission { RoleId = Role.Admin.Id, PermissionId = Permission.WorkflowLogsRead.Id },
            // Messaging: an Admin can hold a conversation with a lead. Granted explicitly
            // rather than folded into leads:write, so a tenant can withhold outward-facing
            // sending from a role that may still edit records.
            new RolePermission { RoleId = Role.Admin.Id, PermissionId = Permission.MessagesSend.Id },
            new RolePermission { RoleId = Role.Admin.Id, PermissionId = Permission.MessagesRead.Id },
            // Owner has read access
            new RolePermission { RoleId = Role.Owner.Id, PermissionId = Permission.UsersRead.Id }
        ]);
    }
}