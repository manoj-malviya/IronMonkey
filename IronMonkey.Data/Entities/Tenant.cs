using IronMonkey.Data.Abstractions;

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
    public string ApprovalStatus { get; private set; } = "Pending";
    public string? ApprovalNote { get; private set; }
    public bool IsProvisioned { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public DateTime? ProvisionedAt { get; private set; }

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
}
