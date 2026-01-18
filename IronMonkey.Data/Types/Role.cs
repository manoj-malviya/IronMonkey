namespace IronMonkey.Data.Types;

public sealed class Role
{
    public static readonly Role Admin = new(1, "Admin");
    public static readonly Role Writer = new(2, "Writer");
    public static readonly Role Publisher = new(3, "Publisher");

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