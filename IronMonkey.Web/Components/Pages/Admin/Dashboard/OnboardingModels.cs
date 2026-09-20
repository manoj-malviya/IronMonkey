namespace IronMonkey.Web.Components.Pages.Admin.Dashboard;

/// <summary>
/// Client-side shapes for the setup-status endpoint. Separate types from the API's records,
/// like the rest of the dashboard models: the web project does not reference the API
/// assembly, and keeping them apart means an added server field cannot break deserialization
/// here.
///
/// Note that nothing in this file decides whether a step is complete — the server does. These
/// are carriers for that answer, which is what stops the dashboard and the API drifting apart
/// about what "configured" means.
/// </summary>
public sealed class OnboardingStatus
{
    public OnboardingTenantContext Tenant { get; set; } = new();
    public List<OnboardingStep> Steps { get; set; } = [];
    public int CompletedCount { get; set; }
    public int TotalCount { get; set; }
    public int CompletionPercentage { get; set; }
    public bool IsComplete { get; set; }
    public bool IsDismissed { get; set; }

    /// <summary>Server's verdict on whether the checklist belongs on screen right now.</summary>
    public bool ShouldShowChecklist { get; set; }

    public DateTime GeneratedAt { get; set; }
}

public sealed class OnboardingTenantContext
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string? SettingsLink { get; set; }
}

public sealed class OnboardingStep
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsComplete { get; set; }
    public string Link { get; set; } = string.Empty;
    public string LinkText { get; set; } = string.Empty;
    public string EmptyStateHint { get; set; } = string.Empty;
}
