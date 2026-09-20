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

namespace IronMonkey.ApiService.Features.UserManagement.Invitations;

/// <summary>
/// Invites someone to join the current tenant.
///
/// The admin supplies a name, an email, a role and an optional note — never a password. The
/// invitee chooses their own credential on the acceptance page, so no one else ever knows it
/// and it is never transmitted.
/// </summary>
public class InviteTeamMemberEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/users/invitations", Handle)
        .WithSummary("Invite a teammate to join the current tenant")
        .WithTags("User Management")
        .RequireAuthorization(PermissionConstants.UsersWrite)
        .WithRequestValidation<Request>();

    public record Request(string Name, string Email, int RoleId, string? Message);

    public record Response(
        Guid InvitationId,
        string Email,
        string Status,
        DateTime SentAt,
        DateTime ExpiresAt,
        string AcceptUrl,
        bool EmailSent,
        string? DeliveryError);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("Name is required.");
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256)
                .WithMessage("A valid email address is required.");
            RuleFor(x => x.RoleId).GreaterThan(0).WithMessage("A valid role ID is required.");
            RuleFor(x => x.Message).MaximumLength(1000);
        }
    }

    internal static async Task<Results<Ok<Response>, ValidationError, NotFound>> Handle(
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IUserContext userContext,
        CentralDbContext centralDb,
        IInvitationService invitations,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        // Normalised exactly as CreateTenantUserEndpoint does: the email is the login
        // identifier and the central index key, so "A@b.com" and "a@b.com" must not become
        // two rows fighting over one index entry.
        var email = request.Email.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        // SuperAdmin is the platform operator's role. A tenant that could invite into it
        // would be minting itself a platform administrator.
        if (!TenantRoleRules.IsVisibleToTenant(request.RoleId))
            return TypedResults.NotFound();

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();

        // Already a member of THIS tenant — including a deactivated one, whose row still
        // holds the address. Safe to state plainly: the caller is an admin of this tenant
        // and can see its own membership list anyway.
        var existsInTenant = await db.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.TenantId == tenantId && u.Email == email, cancellationToken);

        if (existsInTenant)
            return new ValidationError("That person is already a member of this team.");

        // "Outstanding" means the token is still live, which includes a Failed row: its
        // notification did not arrive but its link works, so issuing a second invitation
        // would leave TWO valid tokens for one address. Revoked and Accepted are terminal
        // and do not block a fresh invitation.
        var outstanding = await db.TeamInvitations
            .FirstOrDefaultAsync(
                i => i.Email == email
                     && (i.Status == InvitationStatus.Pending || i.Status == InvitationStatus.Failed)
                     && i.ExpiresAt > now,
                cancellationToken);

        if (outstanding is not null)
            return new ValidationError(
                "An invitation for that address is already pending. Resend or revoke it instead.");

        // The email must be free across the whole platform, because UserTenantIndex maps one
        // email to exactly one tenant. But the response must NOT say that it is taken
        // elsewhere: that would turn this endpoint into an oracle for "does this person have
        // an account on this product", answerable by any tenant admin for any address. The
        // invitation is therefore created and delivered normally, and the collision is
        // resolved at acceptance — where the invitee, who controls the mailbox, is the one
        // told about it.
        var takenElsewhere = await centralDb.UserTenantIndex
            .AsNoTracking()
            .AnyAsync(x => x.Email == email, cancellationToken);

        var issued = InvitationToken.Issue();

        var invitation = TeamInvitation.Create(
            tenantId,
            email,
            request.Name.Trim(),
            request.RoleId,
            string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim(),
            issued.Hash,
            issued.Prefix,
            userContext.UserId,
            now,
            InvitationToken.Lifetime);

        db.TeamInvitations.Add(invitation);

        var tenantName = await centralDb.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "your team";

        InvitationIssued result;

        if (takenElsewhere)
        {
            // Nothing is mailed — the address belongs to an account on another tenant and
            // the invitation can never be accepted. It is recorded as Failed with a reason
            // that does not name the other tenant, so this tenant's admin sees "could not be
            // delivered" and not "they are already on tenant X".
            invitation.MarkDeliveryFailed("The invitation could not be delivered to that address.", now);
            result = new InvitationIssued(
                invitation,
                string.Empty,
                Delivered: false,
                DeliveryError: "The invitation could not be delivered to that address.");
        }
        else
        {
            // Queued through the dispatcher on the same context, so the message row and the
            // invitation row are written by one SaveChanges.
            result = await invitations.DeliverAsync(db, invitation, issued.Plaintext, tenantName, cancellationToken);
        }

        db.UserAuditLogs.Add(UserAuditLog.Record(
            tenantId,
            UserAuditEvent.Invited,
            email,
            now,
            invitationId: invitation.Id,
            actorUserId: userContext.UserId,
            detail: $"Invited as {role.Name}"));

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(
            invitation.Id,
            invitation.Email,
            invitation.Status.ToString(),
            invitation.SentAt,
            invitation.ExpiresAt,
            result.AcceptUrl,
            result.Delivered,
            result.DeliveryError));
    }
}
