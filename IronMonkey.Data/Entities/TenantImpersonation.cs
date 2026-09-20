using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

/// <summary>
/// An audit record of a platform operator borrowing a tenant's identity.
///
/// Written once per issued impersonation token. Deliberately stores no token — only who
/// acted, on which tenant, as which user, and until when. Impersonation tokens are stateless
/// and cannot be revoked, so this table is the record of what was possible, not what was used.
/// </summary>
public sealed class TenantImpersonation : Entity
{
    private TenantImpersonation(
        Guid id,
        Guid platformUserId,
        string platformUserEmail,
        Guid tenantId,
        Guid impersonatedUserId,
        string impersonatedUserEmail,
        DateTime expiresAt,
        string? reason)
        : base(id)
    {
        PlatformUserId = platformUserId;
        PlatformUserEmail = platformUserEmail;
        TenantId = tenantId;
        ImpersonatedUserId = impersonatedUserId;
        ImpersonatedUserEmail = impersonatedUserEmail;
        ExpiresAt = expiresAt;
        Reason = reason;
    }

    private TenantImpersonation() { }

    /// <summary>The platform operator who requested the token.</summary>
    public Guid PlatformUserId { get; private set; }

    public string PlatformUserEmail { get; private set; } = string.Empty;

    public Guid TenantId { get; private set; }

    /// <summary>The tenant user whose identity was assumed.</summary>
    public Guid ImpersonatedUserId { get; private set; }

    public string ImpersonatedUserEmail { get; private set; } = string.Empty;

    public DateTime ExpiresAt { get; private set; }

    public string? Reason { get; private set; }

    public static TenantImpersonation Create(
        Guid platformUserId,
        string platformUserEmail,
        Guid tenantId,
        Guid impersonatedUserId,
        string impersonatedUserEmail,
        DateTime expiresAt,
        string? reason = null)
        => new(
            Guid.NewGuid(),
            platformUserId,
            platformUserEmail,
            tenantId,
            impersonatedUserId,
            impersonatedUserEmail,
            expiresAt,
            reason);
}
