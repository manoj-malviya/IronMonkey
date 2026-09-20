using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Onboarding;

/// <summary>
/// The tenant's first-run setup status: which of the four essential steps are done, how far
/// through it is, and whether this particular user still wants to see the checklist.
///
/// Defined server-side rather than inferred in the browser for three reasons. The rules are
/// then in one place, so the dashboard and anything that asks later cannot disagree about
/// what "configured" means. The counts come from the tenant database through the normal
/// tenant-scoped path, so no page has to be trusted to filter correctly. And a browser that
/// derived this itself would need to fetch users, stages, fields and routing separately —
/// four round trips whose partial failure would show a misleading checklist.
///
/// Every figure is a COUNT against the tenant's own database, taken from the authenticated
/// tenant claim. Nothing here reads CentralDbContext beyond the caller's own tenant row for
/// its display name, and no platform-wide figure is ever returned: a tenant user must not
/// learn how many tenants or users exist outside their own.
///
/// Safe on an empty tenant: every count is zero, every step is incomplete, and the tenant
/// context still renders from the claims, so a freshly provisioned tenant sees four
/// incomplete items with useful hints rather than an error.
/// </summary>
public class GetOnboardingStatusEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/onboarding/setup-status", Handle)
        // Plain authorization, not settings:read. Every member of the tenant lands on /admin
        // and the response carries only counts of the caller's own tenant plus their own
        // name and role — nothing a user of this tenant may not already see. Gating it on a
        // settings permission would 403 the dashboard for ordinary users and leave the page
        // permanently missing a panel.
        .WithSummary("The current tenant's first-run setup status for this user")
        .WithTags("Onboarding")
        .RequireAuthorization();

    internal static async Task<Ok<OnboardingStatusResponse>> Handle(
        ITenantService tenantService,
        IUserContext userContext,
        ITenantDbContextFactory dbContextFactory,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        // The tenant comes from the claim, never from the request. A tenant id parameter here
        // would let any authenticated user read another tenant's setup state.
        var tenantId = tenantService.GetCurrentTenantId();
        var userId = userContext.UserId;
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var status = await OnboardingStatusReader.ReadAsync(
            db, centralDb, tenantId, userId, cancellationToken);

        return TypedResults.Ok(status);
    }
}

/// <summary>
/// Computes the setup status from an already-opened tenant context. Split out from the
/// endpoint so the dismiss and reopen endpoints can return the same freshly-recomputed
/// shape without a second round trip from the browser, and so a test can drive the real
/// rules rather than re-implementing them.
/// </summary>
internal static class OnboardingStatusReader
{
    public static async Task<OnboardingStatusResponse> ReadAsync(
        TenantDbContext db,
        CentralDbContext centralDb,
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        // ── Step 1: at least one additional team member ──────────────────────────────
        //
        // "Additional" means more than the single Admin created at provisioning, so the
        // threshold is two users, not one. Counting one would mark this complete for every
        // tenant the moment it was provisioned and make the step meaningless.
        //
        // The global query filter already excludes soft-deleted users, so a tenant that
        // invited someone and then deactivated them correctly falls back to incomplete.
        var userCount = await db.Users.CountAsync(cancellationToken);
        var hasTeamMember = userCount > 1;

        // ── Step 2: pipeline stages configured ───────────────────────────────────────
        //
        // Only active stages count: a pipeline whose every stage is deactivated is not a
        // working pipeline. A single stage is not a pipeline either — a lead has nowhere to
        // move to — so the threshold is two.
        var activeStageCount = await db.PipelineStages.CountAsync(s => s.IsActive, cancellationToken);
        var hasStages = activeStageCount >= 2;

        // ── Step 3: at least one custom field ────────────────────────────────────────
        //
        // Archived fields do not count. An archived field renders on no form, so a tenant
        // whose only field is archived is capturing nothing custom and has not really
        // completed this step.
        var customFieldCount = await db.CustomFieldDefinitions.CountAsync(f => !f.IsArchived, cancellationToken);
        var hasCustomField = customFieldCount > 0;

        // ── Step 4: lead routing reviewed ────────────────────────────────────────────
        //
        // "Reviewed", not "enabled": a tenant that deliberately turned routing off has made
        // the decision this step exists to prompt. The presence of a RoutingConfig row is
        // that decision — nothing creates one implicitly, it is written only by
        // ConfigureRoutingEndpoint — so an explicitly disabled config still completes.
        var hasRoutingConfig = await db.RoutingConfigs.AnyAsync(cancellationToken);

        var steps = new List<OnboardingStep>
        {
            new(OnboardingStepKeys.TeamMember,
                "Invite your team",
                "Add at least one colleague so work can be assigned and followed up.",
                hasTeamMember,
                "/admin/users",
                "Add a team member",
                "You are the only user right now. Leads assigned to a single person are easy to lose."),

            new(OnboardingStepKeys.PipelineStages,
                "Set up your pipeline",
                "Define the stages a lead moves through, from first contact to closed.",
                hasStages,
                "/admin/configuration?tab=stages",
                "Configure stages",
                activeStageCount == 0
                    ? "No stages yet — leads have nowhere to sit until you add some."
                    : "One stage is not a pipeline: add at least one more so leads can progress."),

            new(OnboardingStepKeys.CustomField,
                "Capture what matters to you",
                "Add a custom field for the detail your business actually records.",
                hasCustomField,
                "/admin/configuration?tab=fields",
                "Add a custom field",
                "Nothing custom is captured yet. Add a field for the detail your team keeps in notes."),

            new(OnboardingStepKeys.LeadRouting,
                "Review lead routing",
                "Decide who new leads go to — round robin, by territory, or nobody for now.",
                hasRoutingConfig,
                "/admin/configuration?tab=routing",
                "Review routing",
                "Routing has never been reviewed, so new leads arrive unassigned."),
        };

        var completed = steps.Count(s => s.IsComplete);
        var total = steps.Count;

        // Integer percentage, floored except at the top. A tenant with 3 of 4 steps must not
        // read "100%" through rounding while an item is still outstanding.
        var percentage = total == 0 ? 100 : (int)Math.Floor(completed * 100.0 / total);
        var isComplete = completed == total;

        var dismissal = await db.TenantOnboardingDismissals
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.UserId == userId, cancellationToken);

        var isDismissed = dismissal?.DismissedAt is not null;

        // Read the caller's own tenant row for its registered name. Filtered on the claim's
        // tenant id, so this is one row — the caller's — and never a listing.
        var tenantName = await centralDb.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .SingleOrDefaultAsync(cancellationToken);

        // The current user and their role come from the tenant database, matched on the
        // identity claim's User.Id. Never on IdentityId, which is "" for every tenant user
        // and would match all of them at once.
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Name,
                u.Email,
                // Roles is a computed property and is not queryable; the mapped navigation
                // is the join table behind it, which projects fine.
                RoleNames = u.Roles.Select(r => r.Name).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        var context = new OnboardingTenantContext(
            tenantId,
            // A platform user impersonating a tenant, or a tenant row that has gone missing,
            // must not blank the strip — fall back rather than throw.
            string.IsNullOrWhiteSpace(tenantName) ? "Your organisation" : tenantName!,
            user?.Name ?? "You",
            user?.Email ?? string.Empty,
            user is null || user.RoleNames.Count == 0
                ? "Member"
                : string.Join(", ", user.RoleNames),
            // The tenant profile/settings capability exists as the configuration workspace's
            // appearance tab, which is where the tenant's own name and branding are edited.
            "/admin/configuration?tab=appearance");

        return new OnboardingStatusResponse(
            context,
            steps,
            completed,
            total,
            percentage,
            isComplete,
            isDismissed,
            // The checklist shows only while steps remain and this user has not dismissed it.
            // Once setup is complete it disappears for everyone without needing a dismissal,
            // which is what keeps it from competing with real CRM work.
            ShouldShowChecklist: !isComplete && !isDismissed,
            DateTime.UtcNow);
    }
}
