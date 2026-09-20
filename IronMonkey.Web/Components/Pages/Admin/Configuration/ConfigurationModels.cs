namespace IronMonkey.Web.Components.Pages.Admin.Configuration;

/// <summary>
/// DTOs shared by the configuration workspace's tab components. They mirror the API
/// responses; the tabs are separate components, so these live outside any one of them.
/// </summary>
public class StageItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsActive { get; set; }
}

public class FieldItem
{
    public Guid Id { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string FieldKey { get; set; } = string.Empty;
    public string FieldType { get; set; } = "Text";
    public string AppliesTo { get; set; } = "Lead";
    public int DisplayOrder { get; set; }
    public bool IsRequired { get; set; }
    public List<string> Options { get; set; } = [];
    public string? HelpText { get; set; }
    public string? DefaultValue { get; set; }
    public bool IsArchived { get; set; }
}

public record StageImpact(
    Guid Id,
    string Name,
    int LeadCount,
    int TransitionCount,
    bool IsOnlyActiveStage,
    bool CanDelete,
    List<ReassignTarget> ReassignTargets);

public record ReassignTarget(Guid Id, string Name);

public record FieldImpact(
    Guid Id,
    string FieldName,
    string AppliesTo,
    int RecordCount,
    List<string> ValuesOutsideOptions,
    bool CanDelete);

/// <summary>Returned by the field update endpoint when a change would invalidate data.</summary>
public record MigrationRequired(
    string Message,
    int AffectedRecords,
    List<string> ValuesOutsideOptions,
    List<string> AvailableStrategies);

public record RoutingConfigResponse(
    Guid Id,
    string Strategy,
    string Dimension,
    string? TerritoryMapJson,
    string? CustomFieldKey,
    bool IsEnabled,
    int RoundRobinPointer);

public record RoutingValidationProblem(string Message, List<string> Errors);

/// <summary>Mirrors ListUsersEndpoint.UserItem — the assignee picker's source.</summary>
public record TenantUserItem(Guid Id, string Name, string Email, string RoleName, bool IsActive)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Email : Name;
}

/// <summary>
/// The save lifecycle a tab reports to the user. Ordering changes in particular need to show
/// that there is something unsaved, not just succeed silently on the next click.
/// </summary>
public enum SaveState { Idle, Unsaved, Saving, Saved, Error }
