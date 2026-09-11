using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Cache;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.RoleManagement;

public class DeleteRoleEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapDelete("/users/roles/{roleId:int}", Handle)
        .WithSummary("Delete a custom role that has no users assigned")
        .WithTags("Role Management")
        .RequireAuthorization();

    public record Response(string Message);

    private static async Task<Results<Ok<Response>, ValidationError, NotFound>> Handle(
        int roleId,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        ICacheService cacheService,
        CancellationToken cancellationToken)
    {
        if (!TenantRoleRules.IsVisibleToTenant(roleId))
            return TypedResults.NotFound();

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var role = await db.Roles
            .Include(r => r.Permissions)
            .SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken);

        if (role is null)
            return TypedResults.NotFound();

        if (TenantRoleRules.IsSystemRole(roleId))
            return new ValidationError($"'{role.Name}' is a built-in role and cannot be deleted.");

        // A user with no roles resolves to no permissions and is locked out of everything,
        // so reassignment is the caller's decision rather than a silent side effect.
        var assignedCount = await db.Roles
            .Where(r => r.Id == roleId)
            .SelectMany(r => r.Users)
            .CountAsync(cancellationToken);

        if (assignedCount > 0)
            return new ValidationError(
                $"{assignedCount} user{(assignedCount == 1 ? " is" : "s are")} assigned to '{role.Name}'. Reassign them first.");

        role.Permissions.Clear();
        db.Roles.Remove(role);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response($"Role '{role.Name}' deleted."));
    }
}
