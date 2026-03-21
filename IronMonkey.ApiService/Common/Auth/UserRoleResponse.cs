using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Common.Auth;

internal sealed class UserRolesResponse
{
    public Guid UserId { get; init; }

    public List<Role> Roles { get; init; } = [];
}