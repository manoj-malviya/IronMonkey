namespace IronMonkey.ApiService.Common.Auth;

public interface IUserContext
{
    Guid UserId { get; }

    string IdentityId { get; }

    Guid TenantId { get; }
}
