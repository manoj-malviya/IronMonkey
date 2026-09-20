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
/// Reissues an invitation with a NEW token and a NEW expiry.
///
/// The old token is overwritten and stops working. Reusing it would mean that "resend" is no
/// remedy for a link that reached the wrong inbox, and that an expired invitation could be
/// revived by nothing more than moving the expiry — the two things a resend most needs to be.
/// </summary>
public class ResendInvitationEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/users/invitations/{id:guid}/resend", Handle)
        .WithSummary("Rotate an invitation's token and send it again")
        .WithTags("User Management")
        .RequireAuthorization(PermissionConstants.UsersWrite);

    public record Response(
        Guid InvitationId,
        string Status,
        DateTime SentAt,
        DateTime ExpiresAt,
        string AcceptUrl,
        bool EmailSent,
        string? DeliveryError);

    internal static async Task<Results<Ok<Response>, ValidationError, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IUserContext userContext,
        CentralDbContext centralDb,
        IInvitationService invitations,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        var now = DateTime.UtcNow;

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        // The tenant query filter is the isolation: another tenant's invitation id is simply
        // absent and gets the same 404 as one that never existed.
        var invitation = await db.TeamInvitations.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (invitation is null)
            return TypedResults.NotFound();

        // A terminal invitation is not resendable. Accepted would mint a second live token
        // for someone who is already a member; revoked would undo the revocation silently.
        if (invitation.Status is InvitationStatus.Accepted or InvitationStatus.Revoked)
            return new ValidationError($"This invitation has been {invitation.Status.ToString().ToLowerInvariant()} and cannot be resent.");

        // Between the original invite and this resend the address may have become a member
        // (accepted through another invitation, or created directly).
        var nowMember = await db.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.TenantId == tenantId && u.Email == invitation.Email, cancellationToken);

        if (nowMember)
            return new ValidationError("That person is already a member of this team.");

        var issued = InvitationToken.Issue();
        invitation.Rotate(issued.Hash, issued.Prefix, userContext.UserId, now, InvitationToken.Lifetime);

        var tenantName = await centralDb.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "your team";

        var result = await invitations.DeliverAsync(db, invitation, issued.Plaintext, tenantName, cancellationToken);

        db.UserAuditLogs.Add(UserAuditLog.Record(
            tenantId,
            UserAuditEvent.InvitationResent,
            invitation.Email,
            now,
            invitationId: invitation.Id,
            actorUserId: userContext.UserId,
            detail: $"Resent (attempt {invitation.SendCount})"));

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(
            invitation.Id,
            invitation.Status.ToString(),
            invitation.SentAt,
            invitation.ExpiresAt,
            result.AcceptUrl,
            result.Delivered,
            result.DeliveryError));
    }
}
