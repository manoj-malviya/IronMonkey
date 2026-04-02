using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.UserManagement;

public class GetUserEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/users/{id:guid}", Handle)
        .WithSummary("Get a single user by ID")
        .RequireAuthorization();

    public record UserDetail(Guid Id, string Name, string Email, int RoleId, string RoleName, bool IsActive, DateTime CreatedAt);

    private static async Task<Results<Ok<UserDetail>, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var user = await db.Users
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && u.Id == id)
            .Include(u => u.Roles)
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
            return TypedResults.NotFound();

        var role = user.Roles.FirstOrDefault();

        return TypedResults.Ok(new UserDetail(
            user.Id,
            user.Name,
            user.Email,
            role?.Id ?? 0,
            role?.Name ?? "None",
            !user.IsDeleted,
            user.CreatedAt));
    }
}
