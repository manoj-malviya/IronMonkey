using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Cache;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.ApiService.Features.UserManagement;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.RoleManagement;

public class UpdateRoleEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/users/roles/{roleId:int}", Handle)
        .WithSummary("Replace a role's permissions, and rename it if it is not a seeded role")
        .WithTags("Role Management")
        .RequireAuthorization(PermissionConstants.UsersWrite)
        .WithRequestValidation<Request>();

    public record Request(string Name, List<int> PermissionIds);
    public record Response(int RoleId, string RoleName, List<string> Permissions);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Role name is required.")
                .MaximumLength(50).WithMessage("Role name must be 50 characters or fewer.");
        }
    }

    internal static async Task<Results<Ok<Response>, ValidationError, NotFound>> Handle(
        int roleId,
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        ICacheService cacheService,
        IUserContext userContext,
        IRecordVisibilityCache visibilityCache,
        CancellationToken cancellationToken)
    {
        // A hidden role must behave as if it does not exist, so a tenant cannot probe for it.
        if (!TenantRoleRules.IsVisibleToTenant(roleId))
            return TypedResults.NotFound();

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        var name = request.Name.Trim();

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var role = await db.Roles
            .Include(r => r.Permissions)
            .SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken);

        if (role is null)
            return TypedResults.NotFound();

        // Seeded roles keep their names: provisioning assigns "Admin" by name, and recipes
        // reference the others. Their grants are still editable.
        if (TenantRoleRules.IsSystemRole(roleId) && !role.Name.Equals(name, StringComparison.Ordinal))
            return new ValidationError($"'{role.Name}' is a built-in role and cannot be renamed.");

        if (!TenantRoleRules.IsSystemRole(roleId))
        {
            if (name.Equals(RoleConstants.SuperAdmin, StringComparison.OrdinalIgnoreCase))
                return new ValidationError($"'{RoleConstants.SuperAdmin}' is a reserved role name.");

            var nameTaken = await db.Roles
                .AnyAsync(r => r.Id != roleId && r.Name.ToLower() == name.ToLower(), cancellationToken);
            if (nameTaken)
                return new ValidationError($"A role named '{name}' already exists.");
        }

        var permissions = await CreateRoleEndpoint.ResolvePermissionsAsync(db, request.PermissionIds, cancellationToken);
        if (permissions is null)
            return new ValidationError("One or more selected permissions are invalid.");

        // Only permissions being ADDED need the actor to hold them: removing a grant the actor
        // lacks is a narrowing, not an escalation.
        var before = role.Permissions.Select(p => p.Name).ToHashSet();
        var added = permissions.Select(p => p.Name).Where(n => !before.Contains(n));
        if (await PrivilegeGuard.CheckCanGrantAsync(db, userContext.UserId, added, cancellationToken) is { } escalation)
            return new ValidationError(escalation);

        // The Admin role must keep user management, or the tenant locks itself out of every
        // role and user change with no way back short of platform intervention.
        if (roleId == AdminSafetyGuard.AdminRoleId
            && PrivilegeGuard.AdminRoleFloor.Any(f => permissions.All(p => p.Name != f)))
            return new ValidationError(
                $"The Admin role must keep {string.Join(" and ", PrivilegeGuard.AdminRoleFloor.Order())} so the tenant can still manage its users.");

        // Role.Name is init-only, so a rename replaces the row rather than mutating it.
        if (!TenantRoleRules.IsSystemRole(roleId) && !role.Name.Equals(name, StringComparison.Ordinal))
        {
            await db.Database.ExecuteSqlAsync(
                $"UPDATE roles SET \"Name\" = {name} WHERE \"Id\" = {roleId}", cancellationToken);
        }

        role.Permissions.Clear();
        foreach (var permission in permissions)
            role.AddPermission(permission);

        PermissionAudit.Record(db, tenantId, userContext.UserId, PermissionAudit.RoleSubject,
            PermissionAudit.RoleSubjectId(roleId), "RolePermissionsChanged", roleId.ToString(),
            new() { ["Name"] = role.Name, ["Permissions"] = before.Order().ToList() },
            new() { ["Name"] = name, ["Permissions"] = permissions.Select(p => p.Name).Order().ToList() });

        await db.SaveChangesAsync(cancellationToken);

        await InvalidatePermissionCacheAsync(db, cacheService, tenantId, roleId, cancellationToken);
        await visibilityCache.InvalidateTenantAsync(tenantId, cancellationToken);

        return TypedResults.Ok(new Response(
            role.Id, name, permissions.Select(p => p.Name).OrderBy(n => n).ToList()));
    }

    /// <summary>
    /// Permissions are cached per user for five minutes, so a grant change would otherwise
    /// take effect at an unpredictable moment. Drop the cache for everyone holding this role.
    /// </summary>
    internal static async Task InvalidatePermissionCacheAsync(
        TenantDbContext db, ICacheService cacheService, Guid tenantId, int roleId,
        CancellationToken cancellationToken)
    {
        // Query from Roles: User.Roles is a computed property and is not queryable.
        var userIds = await db.Roles
            .Where(r => r.Id == roleId)
            .SelectMany(r => r.Users)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        foreach (var userId in userIds)
            await cacheService.RemoveAsync($"auth:permissions-{tenantId}-{userId}", cancellationToken);
    }
}
