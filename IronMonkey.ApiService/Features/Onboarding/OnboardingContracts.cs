namespace IronMonkey.ApiService.Features.Onboarding;

/// <summary>
/// The stable keys for the four setup steps. The UI keys its links and copy off these
/// strings rather than off array order, so adding a step later cannot silently relabel an
/// existing one in an already-open browser.
/// </summary>
public static class OnboardingStepKeys
{
    public const string TeamMember = "team-member";
    public const string PipelineStages = "pipeline-stages";
    public const string CustomField = "custom-field";
    public const string LeadRouting = "lead-routing";
}

/// <summary>
/// One checklist row. <paramref name="Link"/> is the owning page in the web app, so the UI
/// does not carry its own map from step key to route.
/// </summary>
public record OnboardingStep(
    string Key,
    string Title,
    string Description,
    bool IsComplete,
    string Link,
    string LinkText,
    string EmptyStateHint);

/// <summary>
/// The tenant context strip shown above the checklist. Every field here is derived from the
/// authenticated claims and the caller's own tenant — never from a request parameter, so no
/// caller can ask for another tenant's name or another user's role.
/// </summary>
public record OnboardingTenantContext(
    Guid TenantId,
    string TenantName,
    string UserName,
    string UserEmail,
    string RoleName,
    string? SettingsLink);

/// <summary>
/// The server-side setup-status contract. The browser renders this; it does not derive
/// completion itself, so the rules live in exactly one place and cannot drift between the
/// dashboard and anything else that asks later.
/// </summary>
public record OnboardingStatusResponse(
    OnboardingTenantContext Tenant,
    List<OnboardingStep> Steps,
    int CompletedCount,
    int TotalCount,
    int CompletionPercentage,
    bool IsComplete,
    bool IsDismissed,
    /// <summary>
    /// Whether the dashboard should render the checklist right now: steps remain AND this
    /// user has not dismissed it. Computed server-side so "should I show this" is one
    /// decision rather than a boolean expression repeated in every caller.
    /// </summary>
    bool ShouldShowChecklist,
    DateTime GeneratedAt);
