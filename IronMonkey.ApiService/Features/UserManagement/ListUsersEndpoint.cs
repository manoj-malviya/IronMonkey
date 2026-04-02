using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.UserManagement;

public class ListUsersEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/users", Handle)
        .WithSummary("List all users in the current tenant")
        .RequireAuthorization();

    public record UserItem(Guid Id, string Name, string Email, string RoleName, bool IsActive, DateTime CreatedAt);

    private static async Task<Ok<List<UserItem>>> Handle(
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var users = await db.Users
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId)
            .Include(u => u.Roles)
            .OrderBy(u => u.CreatedAt)
            .ToListAsync(cancellationToken);

        var result = users.Select(u => new UserItem(
            u.Id,
            u.Name,
            u.Email,
            u.Roles.FirstOrDefault()?.Name ?? "None",
            !u.IsDeleted,
            u.CreatedAt)).ToList();

        return TypedResults.Ok(result);
    }
}
