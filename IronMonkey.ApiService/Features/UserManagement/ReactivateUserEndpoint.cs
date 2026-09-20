using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.UserManagement;

/// <summary>
/// Restores a deactivated user.
///
/// The counterpart to DeactivateUserEndpoint, and it has to undo BOTH of that endpoint's
/// writes. Clearing the soft delete alone produces a user who looks active in the list and
/// still cannot log in, because deactivation removed their central UserTenantIndex row and
/// login resolves email -> tenant through it.
/// </summary>
public class ReactivateUserEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/users/{id:guid}/reactivate", Handle)
        .WithSummary("Reactivate a deactivated user")
        .WithTags("User Management")
        .RequireAuthorization(PermissionConstants.UsersWrite);

    public record Response(string Message);

    internal static async Task<Results<Ok<Response>, ValidationError, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IUserContext userContext,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        var now = DateTime.UtcNow;

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        // IgnoreQueryFilters is required: the user being reactivated is soft-deleted, so the
        // global filter hides exactly the row this endpoint is for.
        var user = await db.Users
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && u.Id == id)
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
            return TypedResults.NotFound();

        if (!user.IsDeleted)
            return TypedResults.Ok(new Response("User is already active."));

        // While they were deactivated their address may have been claimed — by a new user in
        // this tenant, or by another tenant entirely. Reinstating the index row would make a
        // second row for one email, and LoginEndpoint reads that with SingleOrDefault, so it
        // would break login for BOTH accounts.
        var emailTaken = await centralDb.UserTenantIndex
            .AsNoTracking()
            .AnyAsync(x => x.Email == user.Email, cancellationToken);

        if (emailTaken)
            return new ValidationError(
                "That email address is now in use by another account. Change this user's address before reactivating.");

        user.Reactivate();

        db.UserAuditLogs.Add(UserAuditLog.Record(
            tenantId,
            UserAuditEvent.Reactivated,
            user.Email,
            now,
            targetUserId: user.Id,
            actorUserId: userContext.UserId));

        await db.SaveChangesAsync(cancellationToken);

        // Restores what Deactivate removed. Tenant row first, matching every other path that
        // spans the two databases: a failure here leaves an unusable user rather than an
        // index row pointing at a deactivated one.
        centralDb.UserTenantIndex.Add(UserTenantIndex.Create(user.Email, tenantId));
        await centralDb.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response("User reactivated successfully."));
    }
}
