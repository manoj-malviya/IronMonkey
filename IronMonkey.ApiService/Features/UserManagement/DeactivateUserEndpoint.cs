using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.UserManagement;

public class DeactivateUserEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapDelete("/users/{id:guid}", Handle)
        .WithSummary("Soft-delete (deactivate) a user")
        .RequireAuthorization();

    public record Response(string Message);

    private static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var user = await db.Users
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && u.Id == id)
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
            return TypedResults.NotFound();

        user.Deactivate();
        await db.SaveChangesAsync(cancellationToken);

        // Drop the central index row: a deactivated user must stop resolving at login, and
        // leaving the row behind would also block anyone from reusing that email later.
        var indexRows = await centralDb.UserTenantIndex
            .Where(x => x.Email == user.Email && x.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        if (indexRows.Count > 0)
        {
            centralDb.UserTenantIndex.RemoveRange(indexRows);
            await centralDb.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(new Response("User deactivated successfully."));
    }
}
