namespace IronMonkey.Data.Entities;

public sealed class Role
{
    public static readonly Role Admin = new(1, "Admin");
    public static readonly Role Owner = new(2, "Owner");
    public static readonly Role TeleCaller = new(3, "TeleCaller");

    public Role(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public int Id { get; init; }

    public string Name { get; init; }

    public ICollection<User> Users { get; init; } = new List<User>();

    public ICollection<Permission> Permissions { get; init; } = new List<Permission>();
}