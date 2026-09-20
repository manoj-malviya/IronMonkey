using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Onboarding;

/// <summary>
/// Dismisses or reopens the first-run checklist for the calling user of the calling tenant.
///
/// Server-side rather than in browser storage: the requirement is that the dismissal sticks
/// for that tenant/user, and localStorage sticks only to a browser profile — it would reopen
/// the checklist on a second device or after a cache clear, and the server deciding whether
/// to show the checklist could not see it.
///
/// Both the tenant and the user come from the authenticated claims, so a caller can only ever
/// toggle their own row. There is no user id in the request; accepting one would let any
/// tenant user dismiss (or un-dismiss) the checklist for a colleague.
///
/// Returns the freshly recomputed status so the dashboard re-renders from one round trip
/// rather than a write followed by a read that could interleave with someone else's change.
/// </summary>
public class SetOnboardingDismissalEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/onboarding/dismissal", Handle)
        .WithSummary("Dismiss or reopen the setup checklist for the current user")
        .WithTags("Onboarding")
        .RequireAuthorization();

    public record Request(bool Dismissed);

    internal static async Task<Ok<OnboardingStatusResponse>> Handle(
        Request request,
        ITenantService tenantService,
        IUserContext userContext,
        ITenantDbContextFactory dbContextFactory,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var userId = userContext.UserId;
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        // The global query filter scopes this to the caller's tenant, so a row belonging to
        // another tenant's user with the same id is invisible here and cannot be overwritten.
        var existing = await db.TenantOnboardingDismissals
            .SingleOrDefaultAsync(d => d.UserId == userId, cancellationToken);

        if (request.Dismissed)
        {
            if (existing is null)
            {
                db.TenantOnboardingDismissals.Add(
                    TenantOnboardingDismissal.Create(tenantId, userId));
            }
            else
            {
                existing.Dismiss();
            }
        }
        else if (existing is not null)
        {
            // Reopen clears the timestamp instead of deleting the row, so a later dismissal
            // updates in place rather than racing the unique (TenantId, UserId) index with a
            // second insert.
            existing.Reopen();
        }
        // Reopening when no row exists is already the state being asked for, so nothing is
        // written — an insert here would store "not dismissed", which is what absence means.

        await db.SaveChangesAsync(cancellationToken);

        var status = await OnboardingStatusReader.ReadAsync(
            db, centralDb, tenantId, userId, cancellationToken);

        return TypedResults.Ok(status);
    }
}
