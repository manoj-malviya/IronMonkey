using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.ApiService.Features.UserManagement.Invitations;

/// <summary>
/// What a token turned out to be, for both the preview and the acceptance path.
///
/// One resolver serves both so the preview can never say "valid" about something acceptance
/// would refuse, or vice versa.
/// </summary>
internal enum InvitationCheck
{
    Valid,
    NotFound,
    Expired,
    Revoked,
    AlreadyUsed,
    EmailMismatch,
    AlreadyMember
}

internal static class InvitationResolver
{
    /// <summary>
    /// A single message for every refusal.
    ///
    /// The reason is deliberately not varied by cause on the *public* surface: distinguishing
    /// "no such token" from "revoked" tells an anonymous caller which guesses were real
    /// invitations. The enum is still returned so the server can log and audit precisely.
    /// </summary>
    public const string RefusalMessage =
        "This invitation link is not valid. It may have expired, been revoked, or already been used. Ask your administrator to send a new one.";

    /// <summary>
    /// Finds the invitation a token belongs to, inside one tenant's database.
    ///
    /// Tenant binding is structural: <paramref name="db"/> is built for the tenant named in
    /// the link, and TeamInvitation carries a TenantId query filter, so a token minted by
    /// another tenant is not in scope here and cannot resolve. There is no code path in which
    /// a row from tenant A is reachable through a request naming tenant B.
    /// </summary>
    public static async Task<(InvitationCheck Check, TeamInvitation? Invitation)> ResolveAsync(
        TenantDbContext db,
        Guid tenantId,
        string? token,
        string? expectedEmail,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var prefix = InvitationToken.PrefixOf(token);
        if (prefix is null)
            return (InvitationCheck.NotFound, null);

        // The prefix narrows the search; it is never the credential. Every candidate is then
        // verified against the BCrypt hash, so knowing a prefix gets an attacker nothing.
        var candidates = await db.TeamInvitations
            .Where(i => i.TokenPrefix == prefix)
            .ToListAsync(cancellationToken);

        // Revoked and accepted rows have an EMPTY hash, so Verify returns false for them and
        // they never match here. That is the single-use guarantee at the data level: the only
        // rows a valid token can match are ones that have not been spent.
        var invitation = candidates.FirstOrDefault(i => InvitationToken.Verify(token!, i.TokenHash));

        if (invitation is null)
        {
            // Fall back to identifying a spent row by prefix, purely so the SERVER can record
            // why. The caller still receives the one undifferentiated refusal.
            var spent = candidates.FirstOrDefault();
            return spent?.Status switch
            {
                InvitationStatus.Revoked => (InvitationCheck.Revoked, null),
                InvitationStatus.Accepted => (InvitationCheck.AlreadyUsed, null),
                _ => (InvitationCheck.NotFound, null)
            };
        }

        if (invitation.Status == InvitationStatus.Revoked)
            return (InvitationCheck.Revoked, null);

        if (invitation.Status == InvitationStatus.Accepted)
            return (InvitationCheck.AlreadyUsed, null);

        // Derived from the clock, never from a stored flag: a token stops working the instant
        // it expires, with nothing having had to run to make that true.
        if (invitation.IsExpired(nowUtc))
            return (InvitationCheck.Expired, null);

        // The token is bound to the address it was issued for. Supplying a different email on
        // the acceptance form must not create an account under that other address — otherwise
        // whoever holds the link chooses who joins, and the role assigned to a vetted
        // colleague lands on someone else.
        if (expectedEmail is not null &&
            !string.Equals(invitation.Email, expectedEmail, StringComparison.OrdinalIgnoreCase))
            return (InvitationCheck.EmailMismatch, null);

        // Between issue and redemption the address may have become a member — of this tenant
        // or (through the central index) of any tenant. Either way no account is created.
        var alreadyInTenant = await db.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.TenantId == tenantId && u.Email == invitation.Email, cancellationToken);

        if (alreadyInTenant)
            return (InvitationCheck.AlreadyMember, invitation);

        return (InvitationCheck.Valid, invitation);
    }
}

/// <summary>
/// Redeems an invitation: validates the token and creates the tenant user with the password
/// the invitee chooses here.
///
/// Anonymous by necessity — the invitee has no account yet. The token IS the authentication,
/// which is why it is single-use, expiring, tenant-bound, email-bound and stored only as a
/// hash.
/// </summary>
public class AcceptInvitationEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/invitations/accept", Handle)
        .WithSummary("Accept an invitation and set a password")
        .WithTags("User Management")
        .AllowAnonymous()
        .WithRequestValidation<Request>();

    public record Request(Guid TenantId, string Token, string Email, string Password);
    public record Response(Guid UserId, string Email, string Message);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.TenantId).NotEmpty();
            RuleFor(x => x.Token).NotEmpty();
            RuleFor(x => x.Email).NotEmpty().EmailAddress();
            // The invitee chooses this; no one else ever sees it. A floor is enforced here
            // rather than only in the browser, where it is advisory.
            RuleFor(x => x.Password).NotEmpty().MinimumLength(12)
                .WithMessage("Choose a password of at least 12 characters.");
        }
    }

    internal static async Task<Results<Ok<Response>, ValidationError>> Handle(
        Request request,
        ITenantRegistry tenantRegistry,
        ITenantDbContextFactory dbContextFactory,
        CentralDbContext centralDb,
        ILogger<AcceptInvitationEndpoint> logger,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var email = request.Email.Trim().ToLowerInvariant();

        string connectionString;
        try
        {
            connectionString = await tenantRegistry.GetConnectionStringAsync(request.TenantId, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // An unknown or unprovisioned tenant id gets the same refusal as a bad token —
            // it must not become a probe for which tenant ids exist.
            return new ValidationError(InvitationResolver.RefusalMessage);
        }

        await using var db = dbContextFactory.CreateForTenant(connectionString, request.TenantId);

        var (check, invitation) = await InvitationResolver.ResolveAsync(
            db, request.TenantId, request.Token, email, now, cancellationToken);

        if (check != InvitationCheck.Valid || invitation is null)
        {
            // NOT an exception and NOT a partial write: nothing has been added to the context
            // on any of these paths, so a refusal cannot leave an account behind.
            logger.LogInformation(
                "Invitation acceptance refused for tenant {TenantId}: {Reason}", request.TenantId, check);

            return new ValidationError(InvitationResolver.RefusalMessage);
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == invitation.RoleId, cancellationToken);
        if (role is null)
            return new ValidationError(InvitationResolver.RefusalMessage);

        // The central index maps one email to exactly one tenant, and LoginEndpoint reads it
        // with SingleOrDefault — a second row would break login for BOTH users. If the address
        // has been claimed elsewhere since the invitation was issued, no account is created.
        // The invitee owns this mailbox, so they may be told plainly that the address is in
        // use; a tenant admin could not be, which is why the invite endpoint stays silent.
        var takenElsewhere = await centralDb.UserTenantIndex
            .AsNoTracking()
            .AnyAsync(x => x.Email == email, cancellationToken);

        if (takenElsewhere)
            return new ValidationError(
                "An account already exists for this email address. Sign in instead, or ask your administrator to invite a different address.");

        var user = User.Create(invitation.TenantId, invitation.Name, email, BC.HashPassword(request.Password), role);
        db.Users.Add(user);

        // Marks the token spent and clears the hash, so a replay of the same plaintext cannot
        // match this row again. Written in the SAME SaveChanges as the user, so there is no
        // window in which the account exists and the token is still live.
        invitation.Accept(user.Id, now);

        db.UserAuditLogs.Add(UserAuditLog.Record(
            invitation.TenantId,
            UserAuditEvent.InvitationAccepted,
            email,
            now,
            targetUserId: user.Id,
            invitationId: invitation.Id,
            // No actor: the invitee is not authenticated at this point, so there is no
            // acting user id to record. Null is the honest answer.
            actorUserId: null,
            detail: $"Joined as {role.Name}"));

        await db.SaveChangesAsync(cancellationToken);

        // WITHOUT THIS ROW the new user silently cannot log in: LoginEndpoint resolves
        // email -> tenant through UserTenantIndex and would find nothing, returning
        // "Invalid email address or password" for a perfectly good credential. The two
        // writes span two databases and cannot share a transaction; the tenant row goes
        // first, matching CreateTenantUserEndpoint and provisioning, so a failure here
        // leaves an unusable user rather than an index pointing at nothing.
        centralDb.UserTenantIndex.Add(UserTenantIndex.Create(email, invitation.TenantId));
        await centralDb.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(user.Id, email, "Your account is ready. You can now sign in."));
    }
}
