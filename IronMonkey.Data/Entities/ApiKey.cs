using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

/// <summary>
/// Tenant API key for external REST API access.
/// KeyHash is BCrypt hash of the plaintext key (never store plaintext).
/// TenantId stored in central DB for O(1) lookup by key.
/// </summary>
public sealed class ApiKey : Entity
{
    private ApiKey(Guid id, Guid tenantId, string keyHash, string keyPrefix)
        : base(id)
    {
        TenantId = tenantId;
        KeyHash = keyHash;
        KeyPrefix = keyPrefix;
        IsActive = true;
    }

    private ApiKey() { }

    public Guid TenantId { get; private set; }
    public string KeyHash { get; private set; } = string.Empty;

    /// <summary>First 8 chars of plaintext key for identification in logs/UI. Never full key.</summary>
    public string KeyPrefix { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public static ApiKey Create(Guid tenantId, string keyHash, string keyPrefix)
        => new ApiKey(Guid.NewGuid(), tenantId, keyHash, keyPrefix);

    public void Deactivate() => IsActive = false;
}
