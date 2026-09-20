using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.UserManagement;

public class DeactivateUserEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapDelete("/users/{id:guid}", Handle)
        .WithSummary("Soft-delete (deactivate) a user")
        // users:write, not users:delete: the seeded tenant Admin holds permission 2
        // (users:write) but NOT permission 3 (users:delete) — only the platform SuperAdmin
        // has that one. Gating on users:delete would lock every tenant Admin out of
        // deactivating anyone, which is the one thing this endpoint exists for.
        .RequireAuthorization(PermissionConstants.UsersWrite);

    public record Response(string Message);

    internal static async Task<Results<Ok<Response>, ValidationError, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IUserContext userContext,
        AuthorizationService authorizationService,
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

        if (user.IsDeleted)
            return TypedResults.Ok(new Response("User is already deactivated."));

        // A tenant with no active Admin can no longer invite anyone, change a role, or get
        // its own Admin back without platform intervention. Enforced here rather than only
        // hidden in the UI, because the UI is not the security boundary.
        if (await AdminSafetyGuard.WouldRemoveLastAdminAsync(db, tenantId, id, cancellationToken))
            return new ValidationError(AdminSafetyGuard.LastAdminMessage);

        user.Deactivate();

        db.UserAuditLogs.Add(UserAuditLog.Record(
            tenantId,
            UserAuditEvent.Deactivated,
            user.Email,
            DateTime.UtcNow,
            targetUserId: user.Id,
            actorUserId: userContext.UserId));

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

        // A deactivated user keeps a valid JWT until it expires. Dropping their cached
        // permission set means the next request re-resolves from the tenant DB, where the
        // soft-delete filter now excludes them, so they resolve to no permissions.
        await authorizationService.InvalidatePermissionsAsync(user.Id, tenantId, cancellationToken);

        return TypedResults.Ok(new Response("User deactivated successfully."));
    }
}
