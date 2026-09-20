using IronMonkey.Data.Abstractions;
using IronMonkey.Data.Presentation;

namespace IronMonkey.Data.Entities;

public sealed class Tenant : BaseTenantEntity
{
    private Tenant(Guid id, string name, string slug, string subscriptionPlan, string status)
        : base(id, id)
    {
        Name = name;
        Slug = slug;
        SubscriptionPlan = subscriptionPlan;
        Status = status;
    }

    private Tenant()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string SubscriptionPlan { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public string? DatabaseConnectionString { get; private set; }
    public string? ThemeSettings { get; private set; }

    /// <summary>
    /// What this tenant calls things, how it formats money and dates, and how its shell looks.
    ///
    /// Held on the central row rather than in the tenant database because the web shell needs
    /// it on every render, before any tenant-scoped query has run. Null means the tenant has
    /// configured nothing and every value falls back to the built-in default, so a tenant
    /// provisioned before this existed behaves exactly as it did.
    /// </summary>
    public TenantPresentationSettings? Presentation { get; private set; }
    public string ApprovalStatus { get; private set; } = "Pending";
    public string? ApprovalNote { get; private set; }
    public bool IsProvisioned { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public DateTime? ProvisionedAt { get; private set; }
    public Guid? AppliedRecipeId { get; private set; }
    public int? AppliedRecipeVersion { get; private set; }

    public static Tenant Create(string name, string slug, string subscriptionPlan, string status)
    {
        return new Tenant(Guid.NewGuid(), name, slug, subscriptionPlan, status);
    }

    public void UpdateDatabaseConnectionString(string? connectionString)
    {
        DatabaseConnectionString = connectionString;
    }

    public void UpdateThemeSettings(string? themeSettings)
    {
        ThemeSettings = themeSettings;
    }

    /// <summary>
    /// Replaces the whole presentation block. Branding is validated by the caller before it
    /// reaches here — these values are rendered into pages, so an unvalidated write would be
    /// a stored-XSS vector across every user in the tenant.
    /// </summary>
    public void UpdatePresentation(TenantPresentationSettings? presentation)
    {
        Presentation = presentation;
    }

    public void UpdateSubscriptionPlan(string subscriptionPlan)
    {
        SubscriptionPlan = subscriptionPlan;
    }

    public void UpdateStatus(string status)
    {
        Status = status;
    }

    public void Approve(string? note = null)
    {
        ApprovalStatus = "Approved";
        ApprovalNote = note;
        ApprovedAt = DateTime.UtcNow;
    }

    public void Reject(string? note = null)
    {
        ApprovalStatus = "Rejected";
        ApprovalNote = note;
    }

    public void MarkProvisioned(string connectionString)
    {
        IsProvisioned = true;
        ProvisionedAt = DateTime.UtcNow;
        UpdateDatabaseConnectionString(connectionString);
    }

    public void SetAppliedRecipe(Guid recipeId, int recipeVersion)
    {
        AppliedRecipeId = recipeId;
        AppliedRecipeVersion = recipeVersion;
    }
}
