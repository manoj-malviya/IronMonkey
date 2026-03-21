using Microsoft.AspNetCore.Authorization;

namespace IronMonkey.ApiService.Common.Auth;
internal sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }
    public string Permission { get; }
}