using Microsoft.AspNetCore.Authorization;
using IronMonkey.ApiService.Common.Extensions;

namespace IronMonkey.ApiService.Common.Auth;

internal sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IServiceProvider _serviceProvider;

    public PermissionAuthorizationHandler(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity is not { IsAuthenticated: true })
        {
            return;
        }

        using IServiceScope scope = _serviceProvider.CreateScope();

        AuthorizationService authorizationService = scope.ServiceProvider.GetRequiredService<AuthorizationService>();

        string identityId = context.User.GetIdentityId();

        // Guid.Empty means a platform operator, which is a valid caller here —
        // its permissions come from the central DB rather than a tenant DB.
        Guid tenantId = context.User.GetTenantId() ?? Guid.Empty;

        HashSet<string> permissions = await authorizationService.GetPermissionsForUserAsync(identityId, tenantId);

        if (permissions.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}