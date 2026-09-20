using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.UserManagement.Invitations;

/// <summary>
/// The tenant's invitations, with the state, dates and next action the list page renders.
///
/// Never returns a token or a hash. The acceptance link is shown once, in the response to the
/// invite or resend that minted it — there is nothing here to re-derive it from, which is the
/// point of storing only the hash.
/// </summary>
public class ListInvitationsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/users/invitations", Handle)
        .WithSummary("List invitations for the current tenant")
        .WithTags("User Management")
        .RequireAuthorization(PermissionConstants.UsersRead);

    public record InvitationItem(
        Guid Id,
        string Name,
        string Email,
        int RoleId,
        string RoleName,
        string Status,
        DateTime SentAt,
        DateTime ExpiresAt,
        DateTime? AcceptedAt,
        int SendCount,
        string? FailureReason,
        string NextAction);

    internal static async Task<Ok<List<InvitationItem>>> Handle(
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        // bool? not bool: a non-nullable bool query parameter is REQUIRED in minimal APIs,
        // so every caller that omitted it would get a 500.
        bool? includeResolved,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        var now = DateTime.UtcNow;

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var roleNames = await db.Roles
            .AsNoTracking()
            .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        var query = db.TeamInvitations.AsNoTracking();

        if (includeResolved != true)
        {
            // "Resolved" means done with: accepted or revoked. An expired one is still shown
            // by default, because it is the row the admin has to act on.
            query = query.Where(i =>
                i.Status != InvitationStatus.Accepted && i.Status != InvitationStatus.Revoked);
        }

        var rows = await query
            .OrderByDescending(i => i.SentAt)
            .ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);

        var items = rows.Select(i =>
        {
            // Expiry is derived from the clock, not stored, so a Pending row past its expiry
            // reports Expired here without any sweeper having had to run.
            var status = i.IsExpired(now) ? "Expired" : i.Status.ToString();

            return new InvitationItem(
                i.Id,
                i.Name,
                i.Email,
                i.RoleId,
                roleNames.TryGetValue(i.RoleId, out var rn) ? rn : "Unknown",
                status,
                i.SentAt,
                i.ExpiresAt,
                i.AcceptedAt,
                i.SendCount,
                i.FailureReason,
                NextActionFor(status));
        }).ToList();

        return TypedResults.Ok(items);
    }

    private static string NextActionFor(string status) => status switch
    {
        "Pending" => "Awaiting acceptance — resend or revoke",
        "Expired" => "Resend to issue a new link",
        "Failed" => "Delivery failed — copy the link or resend",
        "Accepted" => "None",
        "Revoked" => "None",
        _ => "None"
    };
}
