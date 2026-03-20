using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

/// <summary>
/// Central index that maps a user's email to their tenant.
/// Populated when a user is created in a tenant DB.
/// Enables the login flow: email -> tenantId lookup (no need to scan all tenant DBs).
/// </summary>
public sealed class UserTenantIndex : Entity
{
    private UserTenantIndex(Guid id, string email, Guid tenantId) : base(id)
    {
        Email = email;
        TenantId = tenantId;
    }

    private UserTenantIndex() { }

    public string Email { get; private set; } = string.Empty;
    public Guid TenantId { get; private set; }

    public static UserTenantIndex Create(string email, Guid tenantId)
        => new(Guid.NewGuid(), email, tenantId);
}
