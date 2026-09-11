using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.UserManagement;

/// <summary>
/// Lists the roles available inside the current tenant, for the user create/edit forms.
///
/// Replaces the legacy <c>GET /roles</c>, which queries the obsolete AppDbContext against the
/// central database — where the tenant-scoped Roles table does not exist, so it returns 500.
/// Roles are seeded per tenant by migration (SuperAdmin=1, Admin=201, Owner=301, TeleCaller=302).
/// </summary>
public class ListTenantRolesEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/users/roles", Handle)
        .WithSummary("List the roles available in the current tenant")
        .WithTags("User Management")
        .RequireAuthorization();

    public record RoleItem(
        int RoleId,
        string RoleName,
        List<string> Permissions,
        bool IsSystemRole,
        int UserCount);

    private static async Task<Ok<List<RoleItem>>> Handle(
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        // User has a soft-delete query filter, so counting through the Users navigation
        // inside the projection makes EF throw. Load the rows, then count separately.
        var roles = await db.Roles
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .Select(r => new
            {
                r.Id,
                r.Name,
                Permissions = r.Permissions.Select(p => p.Name).ToList()
            })
            .ToListAsync(cancellationToken);

        var userCounts = await db.Users
            .AsNoTracking()
            .SelectMany(u => u.Roles.Select(r => r.Id))
            .GroupBy(id => id)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, cancellationToken);

        // SuperAdmin is the platform operator's role; a tenant must not see, assign or edit it.
        // admin:access is filtered for the same reason.
        var visible = roles
            .Where(r => TenantRoleRules.IsVisibleToTenant(r.Id))
            .Select(r => new RoleItem(
                r.Id,
                r.Name,
                r.Permissions
                    .Where(n => !TenantRoleRules.HiddenPermissions.Contains(n))
                    .OrderBy(n => n).ToList(),
                TenantRoleRules.SystemRoleIds.Contains(r.Id),
                userCounts.TryGetValue(r.Id, out var c) ? c : 0))
            .ToList();

        return TypedResults.Ok(visible);
    }

    // Test-accessible handler — same logic as Handle, exposed for integration testing
    internal static async Task<List<RoleItem>> HandleForTest(
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var okResult = await Handle(tenantService, dbContextFactory, cancellationToken);
        return okResult.Value!;
    }
}
