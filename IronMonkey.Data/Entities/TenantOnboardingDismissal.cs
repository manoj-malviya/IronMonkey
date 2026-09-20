using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

/// <summary>
/// Records that one user of one tenant has dismissed the first-run setup checklist.
///
/// This lives in the tenant database rather than in browser storage because the requirement
/// is that a dismissal sticks for that tenant/user — not for that browser. localStorage would
/// reopen the checklist on a second device, after a cache clear, or in a private window, and
/// would be invisible to the server that decides whether the checklist is still relevant.
///
/// It is keyed on <see cref="UserId"/>, which is <c>User.Id</c> — the value LoginEndpoint puts
/// in the identity claim. It is deliberately NOT keyed on <c>User.IdentityId</c>, which is ""
/// for every tenant user and would collapse every member of the tenant onto one row, so one
/// person dismissing would hide the checklist from their colleagues.
///
/// Absence of a row means "not dismissed", which is what makes this additive: a tenant
/// provisioned before this existed simply sees the checklist.
/// </summary>
public sealed class TenantOnboardingDismissal : BaseTenantEntity
{
    private TenantOnboardingDismissal() { }

    /// <summary>The tenant user who dismissed it — <c>User.Id</c>, not <c>IdentityId</c>.</summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// When the checklist was last dismissed, or null if it has since been reopened.
    ///
    /// Reopening clears this rather than deleting the row, so the history of "this user has
    /// seen and dismissed onboarding before" survives a reopen and the row can be toggled
    /// without a second insert racing the unique index.
    /// </summary>
    public DateTime? DismissedAt { get; private set; }

    public bool IsDismissed => DismissedAt is not null;

    public static TenantOnboardingDismissal Create(Guid tenantId, Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = userId,
        DismissedAt = DateTime.UtcNow
    };

    public void Dismiss() => DismissedAt = DateTime.UtcNow;

    public void Reopen() => DismissedAt = null;
}
