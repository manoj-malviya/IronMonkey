using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.RoleManagement;

public class CreateRoleEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/users/roles", Handle)
        .WithSummary("Create a role in the current tenant with the given permissions")
        .WithTags("Role Management")
        .RequireAuthorization()
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

    private static async Task<Results<Created<Response>, ValidationError>> Handle(
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        var name = request.Name.Trim();

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        // Role names double as the JWT role claim, so a tenant role called "SuperAdmin"
        // would impersonate the platform operator in every role-based check.
        if (name.Equals(RoleConstants.SuperAdmin, StringComparison.OrdinalIgnoreCase))
            return new ValidationError($"'{RoleConstants.SuperAdmin}' is a reserved role name.");

        var nameTaken = await db.Roles.AnyAsync(r => r.Name.ToLower() == name.ToLower(), cancellationToken);
        if (nameTaken)
            return new ValidationError($"A role named '{name}' already exists.");

        var permissions = await ResolvePermissionsAsync(db, request.PermissionIds, cancellationToken);
        if (permissions is null)
            return new ValidationError("One or more selected permissions are invalid.");

        // Roles have a non-generated int key, so the id is allocated here. Custom roles start
        // above the seeded band (1..302) so they can never collide with a future seeded role.
        var maxId = await db.Roles
            .Where(r => r.Id >= TenantRoleRules.CustomRoleIdFloor)
            .Select(r => (int?)r.Id)
            .MaxAsync(cancellationToken) ?? TenantRoleRules.CustomRoleIdFloor - 1;

        var role = Role.Create(maxId + 1, name);
        foreach (var permission in permissions)
            role.AddPermission(permission);

        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/users/roles/{role.Id}",
            new Response(role.Id, role.Name, permissions.Select(p => p.Name).OrderBy(n => n).ToList()));
    }

    /// <summary>
    /// Loads the requested permissions, returning null if any id is unknown or is one a
    /// tenant may not grant. Shared with the update endpoint.
    /// </summary>
    internal static async Task<List<Permission>?> ResolvePermissionsAsync(
        TenantDbContext db, List<int> permissionIds, CancellationToken cancellationToken)
    {
        var ids = permissionIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        var permissions = await db.Set<Permission>()
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken);

        if (permissions.Count != ids.Count)
            return null;

        // Rejected server-side, not merely hidden in the UI: a hand-crafted request must not
        // be able to grant a tenant platform administration.
        if (permissions.Any(p => !TenantRoleRules.IsGrantableByTenant(p.Name)))
            return null;

        return permissions;
    }
}
