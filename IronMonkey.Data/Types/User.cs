using Microsoft.AspNetCore.Identity;
using IronMonkey.Common;
using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Types;

public sealed class User : Entity
{
    private readonly List<Role> _roles = new();

    private User(Guid id, string name, string email, string password)
        : base(id)
    {
        Name = name;
        Email = email;
        Password = password;
    }

    private User()
    {
    }

    public string Name { get; private set; }
    public string Email { get; private set; }
    public string Password { get; private set; }

    public string IdentityId { get; private set; } = string.Empty;

    public IReadOnlyCollection<Role> Roles => _roles.ToList(); //shadow property

    public static User Create(string name, string email, string password, Role role)
    {
        var user = new User(Guid.NewGuid(), name, email, password);

        //user.RaiseDomainEvent(new UserCreatedDomainEvent(user.Id));

        user._roles.Add(role);

        return user;
    }

    public void SetIdentityId(string identityId)
    {
        IdentityId = identityId;
    }
}