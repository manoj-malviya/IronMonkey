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
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.QuotesApprove.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.CatalogWrite.Id },
            new RolePermission { RoleId = Role.SuperAdmin.Id, PermissionId = Permission.DataExport.Id },
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
            // Quote discount approval: the tenant administrator is the default approver. A
            // tenant that wants a Sales Manager to approve grants this to that role instead.
            new RolePermission { RoleId = Role.Admin.Id, PermissionId = Permission.QuotesApprove.Id },
            // Catalog and quote settings: everyday sales administration, which a tenant Admin
            // must be able to run — Admin does not hold settings:write.
            new RolePermission { RoleId = Role.Admin.Id, PermissionId = Permission.CatalogWrite.Id },
            // Export: Admin only by default. Taking the book out of the CRM is a separate act
            // from reading it on screen, so other roles must be granted it deliberately.
            new RolePermission { RoleId = Role.Admin.Id, PermissionId = Permission.DataExport.Id },
            // Owner has read access
            new RolePermission { RoleId = Role.Owner.Id, PermissionId = Permission.UsersRead.Id },
            // CRM record access for Owner and TeleCaller. Before record permissions were
            // enforced, every authenticated user could read and edit leads, contacts and
            // opportunities whatever their role held. These grants make that existing access
            // explicit, so enforcing leads:* / contacts:* / opportunities:* changes nothing
            // for a tenant until an Admin chooses to narrow a role. (Custom roles get the same
            // backfill in the RecordVisibility migration.)
            new RolePermission { RoleId = Role.Owner.Id, PermissionId = Permission.LeadsRead.Id },
            new RolePermission { RoleId = Role.Owner.Id, PermissionId = Permission.LeadsWrite.Id },
            new RolePermission { RoleId = Role.Owner.Id, PermissionId = Permission.ContactsRead.Id },
            new RolePermission { RoleId = Role.Owner.Id, PermissionId = Permission.ContactsWrite.Id },
            new RolePermission { RoleId = Role.Owner.Id, PermissionId = Permission.OpportunitiesRead.Id },
            new RolePermission { RoleId = Role.Owner.Id, PermissionId = Permission.OpportunitiesWrite.Id },
            new RolePermission { RoleId = Role.TeleCaller.Id, PermissionId = Permission.LeadsRead.Id },
            new RolePermission { RoleId = Role.TeleCaller.Id, PermissionId = Permission.LeadsWrite.Id },
            new RolePermission { RoleId = Role.TeleCaller.Id, PermissionId = Permission.ContactsRead.Id },
            new RolePermission { RoleId = Role.TeleCaller.Id, PermissionId = Permission.ContactsWrite.Id },
            new RolePermission { RoleId = Role.TeleCaller.Id, PermissionId = Permission.OpportunitiesRead.Id },
            new RolePermission { RoleId = Role.TeleCaller.Id, PermissionId = Permission.OpportunitiesWrite.Id }
        ]);
    }
}