using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.UserManagement.Invitations;

/// <summary>
/// Tells the acceptance page what it is showing, before the invitee types anything.
///
/// Anonymous, like acceptance itself, and it creates nothing. It discloses only what the
/// holder of the token was already sent by email — the team name, their own name and the
/// address the invitation is bound to — so it adds no information an attacker could not get
/// from the link they already hold. Anything it refuses, acceptance refuses too: both go
/// through <see cref="InvitationResolver"/>.
/// </summary>
public class PreviewInvitationEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/invitations/preview", Handle)
        .WithSummary("Describe an invitation token without redeeming it")
        .WithTags("User Management")
        .AllowAnonymous();

    public record Response(
        string Name,
        string Email,
        string RoleName,
        string TenantName,
        string? Message,
        DateTime ExpiresAt);

    internal static async Task<Results<Ok<Response>, ValidationError>> Handle(
        Guid tenant,
        string? token,
        ITenantRegistry tenantRegistry,
        ITenantDbContextFactory dbContextFactory,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        string connectionString;
        try
        {
            connectionString = await tenantRegistry.GetConnectionStringAsync(tenant, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return new ValidationError(InvitationResolver.RefusalMessage);
        }

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenant);

        // No expected email: the page has not collected one yet, so the email check is left
        // to acceptance. Every other check — revoked, expired, already used, wrong tenant —
        // applies here exactly as it will there.
        var (check, invitation) = await InvitationResolver.ResolveAsync(
            db, tenant, token, expectedEmail: null, now, cancellationToken);

        if (check != InvitationCheck.Valid || invitation is null)
            return new ValidationError(InvitationResolver.RefusalMessage);

        var roleName = await db.Roles
            .AsNoTracking()
            .Where(r => r.Id == invitation.RoleId)
            .Select(r => r.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "Member";

        var tenantName = await centralDb.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenant)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "your team";

        return TypedResults.Ok(new Response(
            invitation.Name,
            invitation.Email,
            roleName,
            tenantName,
            invitation.Message,
            invitation.ExpiresAt));
    }
}
