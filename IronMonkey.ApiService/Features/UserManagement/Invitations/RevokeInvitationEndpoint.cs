using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.UserManagement.Invitations;

/// <summary>
/// Withdraws an outstanding invitation. The token stops validating immediately — the row's
/// hash is cleared as well as its status changed, so a redemption attempt cannot match even
/// if a future code path neglects the status check.
/// </summary>
public class RevokeInvitationEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapDelete("/users/invitations/{id:guid}", Handle)
        .WithSummary("Revoke an outstanding invitation")
        .WithTags("User Management")
        .RequireAuthorization(PermissionConstants.UsersWrite);

    public record Response(string Message);

    internal static async Task<Results<Ok<Response>, ValidationError, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        var now = DateTime.UtcNow;

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var invitation = await db.TeamInvitations.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (invitation is null)
            return TypedResults.NotFound();

        // Revoking an accepted invitation would suggest it undoes the membership, which it
        // does not — the user already exists and must be deactivated instead.
        if (invitation.Status == InvitationStatus.Accepted)
            return new ValidationError("This invitation has already been accepted. Deactivate the user instead.");

        if (invitation.Status == InvitationStatus.Revoked)
            return TypedResults.Ok(new Response("Invitation already revoked."));

        invitation.Revoke(now);

        db.UserAuditLogs.Add(UserAuditLog.Record(
            tenantId,
            UserAuditEvent.InvitationRevoked,
            invitation.Email,
            now,
            invitationId: invitation.Id,
            actorUserId: userContext.UserId));

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response("Invitation revoked."));
    }
}
