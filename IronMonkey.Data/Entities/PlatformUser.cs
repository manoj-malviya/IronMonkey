using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

/// <summary>
/// A platform operator that lives in the central DB and belongs to no tenant.
/// Distinct from <see cref="User"/>, which is a tenant member stored in a tenant DB.
/// Used for platform administration — approving signup requests and provisioning tenants.
/// </summary>
public sealed class PlatformUser : Entity
{
    private PlatformUser(Guid id, string name, string email, string passwordHash, string role)
        : base(id)
    {
        Name = name;
        Email = email;
        PasswordHash = passwordHash;
        Role = role;
    }

    private PlatformUser() { }

    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>Platform role name — see <see cref="Role.SuperAdmin"/>.</summary>
    public string Role { get; private set; } = string.Empty;

    public DateTime? LastLoginAt { get; private set; }

    public static PlatformUser Create(string name, string email, string passwordHash, string role)
        => new(Guid.NewGuid(), name, email, passwordHash, role);

    public void ResetPassword(string passwordHash)
    {
        PasswordHash = passwordHash;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RecordLogin()
    {
        LastLoginAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
