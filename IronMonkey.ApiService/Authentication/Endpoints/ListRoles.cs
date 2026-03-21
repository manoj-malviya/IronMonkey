using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using IronMonkey.ApiService.Common;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class ListRoles : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/roles", Handle)
        .WithSummary("Lists all roles in the system");

    private static async Task<Ok<List<Response>>> Handle(AppDbContext dbContext)
    {
        var roles = await dbContext.Set<Role>()
            .Select(role => new Response(role.Id, role.Name))
            .AsAsyncEnumerable()
            .ToListAsync();

        return TypedResults.Ok(roles);
    }

    public record Response(int RoleId, string RoleName);
}