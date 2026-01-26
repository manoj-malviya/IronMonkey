namespace IronMonkey.Data.Entities;

public sealed class Role
{
    public static readonly Role SuperAdmin = new(1, "SuperAdmin");
    public static readonly Role Admin = new(201, "Admin");
    public static readonly Role Owner = new(301, "Owner");
    public static readonly Role TeleCaller = new(302, "TeleCaller");

    private Role(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public int Id { get; init; }

    public string Name { get; init; }

    public ICollection<User> Users { get; init; } = new List<User>();

    public ICollection<Permission> Permissions { get; init; } = new List<Permission>();

    public static Role Create(int id, string name)
    {
        return new Role(id, name);
    }

    public void AddPermission(Permission permission)
    {
        if (!Permissions.Contains(permission))
        {
            Permissions.Add(permission);
        }
    }
}