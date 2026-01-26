using IronMonkey.Data;
using IronMonkey.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class ListRolePermissions : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/roles/{roleId}/permissions", Handle)
        .WithSummary("Lists all permissions attached to a role");

    private static async Task<Results<Ok<List<Response>>, NotFound>> Handle(int roleId, AppDbContext dbContext)
    {
        var role = await dbContext.Set<Role>()
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Id == roleId);

        if (role == null)
        {
            return TypedResults.NotFound();
        }

        var permissions = role.Permissions
            .Select(permission => new Response(permission.Id, permission.Name))
            .ToList();

        return TypedResults.Ok(permissions);
    }

    public record Response(int PermissionId, string PermissionName);
}