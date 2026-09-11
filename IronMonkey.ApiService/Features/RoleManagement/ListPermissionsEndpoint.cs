using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.RoleManagement;

/// <summary>
/// The permission catalogue a tenant may grant, grouped for display. admin:access is
/// excluded — see <see cref="TenantRoleRules"/>.
/// </summary>
public class ListPermissionsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/permissions", Handle)
        .WithSummary("List the permissions a tenant can grant, grouped by resource")
        .WithTags("Role Management")
        .RequireAuthorization();

    public record PermissionItem(int Id, string Name, string Group, string Action);

    private static async Task<Ok<List<PermissionItem>>> Handle(
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var permissions = await db.Set<Data.Entities.Permission>()
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .ToListAsync(cancellationToken);

        // Names are "resource:action", e.g. "leads:write".
        var result = permissions
            .Where(p => TenantRoleRules.IsGrantableByTenant(p.Name))
            .Select(p =>
            {
                var parts = p.Name.Split(':', 2);
                var group = parts.Length == 2 ? parts[0] : p.Name;
                var action = parts.Length == 2 ? parts[1] : string.Empty;
                return new PermissionItem(p.Id, p.Name, group, action);
            })
            .ToList();

        return TypedResults.Ok(result);
    }
}
