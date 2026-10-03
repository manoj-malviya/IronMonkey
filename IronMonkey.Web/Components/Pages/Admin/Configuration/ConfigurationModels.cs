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

    /// <summary>"Entry", "Active", "ClosedWon" or "ClosedLost". Lead stages leave it at the
    /// default; deal stages edit it, because won/lost is decided by this and never by the
    /// stage's name.</summary>
    public string StageType { get; set; } = "Active";

    public bool IsTerminal { get; set; }

    /// <summary>The pipeline this stage belongs to. Present on every stage response.</summary>
    public Guid PipelineId { get; set; }

    public string PipelineName { get; set; } = string.Empty;
}

/// <summary>
/// One of the tenant's named pipelines, as the picker shows it.
///
/// The picker itself only renders when <see cref="PipelineListResponse.IsMultiPipeline"/> is
/// true — a tenant with one pipeline is never asked to choose between one option.
/// </summary>
public record PipelineItem(
    Guid Id, string Name, string RecordType, bool IsDefault, bool IsActive,
    int Order, string? Description, int StageCount, int RecordCount);

public record PipelineListResponse(
    List<PipelineItem> Items, bool IsMultiPipeline, Guid? DefaultPipelineId);

/// <summary>The blast radius of removing a pipeline, shown before anything is removed.</summary>
public record PipelineImpact(
    Guid Id,
    string Name,
    string RecordType,
    int StageCount,
    int RecordCount,
    int ScopedFieldCount,
    int ScopedRuleCount,
    int ScopedRoutingCount,
    int HistoryCount,
    bool IsDefault,
    bool IsOnlyPipeline,
    bool CanRemove,
    string? Blocker,
    List<PipelineReassignTarget> ReassignTargets);

public record PipelineReassignTarget(Guid Id, string Name, Guid? EntryStageId, string? EntryStageName);

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

/// <param name="LeadCount">
/// Records in the stage — leads for a lead stage, opportunities for a deal stage. The API
/// names the opportunity one OpportunityCount, so the deal tab maps it across; one property
/// here because every caller asks the same question.
/// </param>
public record StageImpact(
    Guid Id,
    string Name,
    int LeadCount,
    int TransitionCount,
    bool IsOnlyActiveStage,
    bool CanDelete,
    List<ReassignTarget> ReassignTargets);

public record ReassignTarget(Guid Id, string Name);

/// <summary>
/// The deal-stage impact response. Separate from <see cref="StageImpact"/> only because the
/// API names its record count <c>OpportunityCount</c> — the rules it feeds (reassign before
/// removing, never remove the last active stage) are identical, deliberately so.
/// </summary>
public record OpportunityStageImpact(
    Guid Id,
    string Name,
    int OpportunityCount,
    int HistoryCount,
    bool IsOnlyActiveStage,
    bool CanDelete,
    List<ReassignTarget> ReassignTargets);

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
