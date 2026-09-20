namespace IronMonkey.ApiService.Common.Auth;

public interface IUserContext
{
    Guid UserId { get; }

    string IdentityId { get; }

    Guid TenantId { get; }

    /// <summary>
    /// The platform user behind an impersonation token, or null for an ordinary caller.
    /// Unlike the other members this never throws — absence is the normal case.
    /// </summary>
    string? ActAs { get; }
}
