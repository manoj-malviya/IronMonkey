using IronMonkey.Common.Auth;

namespace IronMonkey.Data.Entities;

public sealed class Permission
{
    public static readonly Permission UsersRead = new(1, PermissionConstants.UsersRead);
    public static readonly Permission UsersWrite = new(2, PermissionConstants.UsersWrite);
    public static readonly Permission UsersDelete = new(3, PermissionConstants.UsersDelete);
    public static readonly Permission LeadsRead = new(4, PermissionConstants.LeadsRead);
    public static readonly Permission LeadsWrite = new(5, PermissionConstants.LeadsWrite);
    public static readonly Permission LeadsDelete = new(6, PermissionConstants.LeadsDelete);
    public static readonly Permission ContactsRead = new(7, PermissionConstants.ContactsRead);
    public static readonly Permission ContactsWrite = new(8, PermissionConstants.ContactsWrite);
    public static readonly Permission ContactsDelete = new(9, PermissionConstants.ContactsDelete);
    public static readonly Permission OpportunitiesRead = new(10, PermissionConstants.OpportunitiesRead);
    public static readonly Permission OpportunitiesWrite = new(11, PermissionConstants.OpportunitiesWrite);
    public static readonly Permission OpportunitiesDelete = new(12, PermissionConstants.OpportunitiesDelete);
    public static readonly Permission ReportsRead = new(13, PermissionConstants.ReportsRead);
    public static readonly Permission ReportsWrite = new(14, PermissionConstants.ReportsWrite);
    public static readonly Permission SettingsRead = new(15, PermissionConstants.SettingsRead);
    public static readonly Permission SettingsWrite = new(16, PermissionConstants.SettingsWrite);
    public static readonly Permission AdminAccess = new(17, PermissionConstants.AdminAccess);

    private Permission(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public int Id { get; init; }

    public string Name { get; init; }

    public static Permission Create(int id, string name)
    {
        return new Permission(id, name);
    }
}