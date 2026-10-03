namespace IronMonkey.Web.Components.Pages.Admin.Dashboard;

/// <summary>
/// Client-side shapes for the four dashboard endpoints. Deliberately separate types from
/// the API's records: the web project does not reference the API assembly, and keeping
/// them apart means an added server field cannot silently break deserialization here.
/// </summary>
public sealed class DashboardSummary
{
    public DateTime RangeFrom { get; set; }
    public DateTime RangeTo { get; set; }
    public string Preset { get; set; } = string.Empty;
    public int TotalLeads { get; set; }
    public int OpenLeads { get; set; }
    public int ConvertedLeads { get; set; }
    public decimal ConversionRate { get; set; }
    public int TotalContacts { get; set; }
    public bool HasAnyStages { get; set; }
    public List<StageBreakdownItem> ByStage { get; set; } = [];
    public DateTime GeneratedAt { get; set; }
}

public sealed class StageBreakdownItem
{
    public Guid StageId { get; set; }
    public string StageName { get; set; } = string.Empty;
    public int Order { get; set; }
    public string StageType { get; set; } = string.Empty;
    public bool IsTerminal { get; set; }
    public int LeadCount { get; set; }
}

public sealed class DashboardOpportunities
{
    public int TotalCount { get; set; }
    public decimal TotalValue { get; set; }
    public int OpenCount { get; set; }
    public decimal OpenValue { get; set; }
    public int WonCount { get; set; }
    public decimal WonValue { get; set; }
    public List<OpportunityStageItem> ByStage { get; set; } = [];

    /// <summary>
    /// Per-pipeline subtotals, present whenever the figures span more than one pipeline.
    /// Derived server-side from the same rows as the totals, so the parts always sum to the
    /// whole — which is what lets the widget show the split rather than one number whose
    /// composition the reader has to guess at.
    /// </summary>
    public List<PipelineBreakdownItem> ByPipeline { get; set; } = [];

    /// <summary>What these figures cover — a pipeline name, or "All pipelines".</summary>
    public string ScopeLabel { get; set; } = string.Empty;

    public Guid? PipelineId { get; set; }
    public bool IsTenantWide { get; set; }

    /// <summary>
    /// Whether the tenant has more than one deal pipeline. False means the widget shows no
    /// scope label and no picker at all — a single-pipeline tenant's dashboard is unchanged.
    /// </summary>
    public bool IsMultiPipeline { get; set; }

    public DateTime GeneratedAt { get; set; }

    /// <summary>
    /// Deals in another currency with no recorded exchange rate. Counted, but left out of
    /// every value above rather than added at face value — the widget says so.
    /// </summary>
    public int UnconvertedCount { get; set; }
    public Dictionary<string, decimal>? UnconvertedAmounts { get; set; }
}

public sealed class PipelineBreakdownItem
{
    public Guid PipelineId { get; set; }
    public string PipelineName { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal TotalValue { get; set; }
    public int OpenCount { get; set; }
    public decimal OpenValue { get; set; }
    public int WonCount { get; set; }
    public decimal WonValue { get; set; }
}

public sealed class OpportunityStageItem
{
    public Guid StageId { get; set; }
    public string Stage { get; set; } = string.Empty;

    /// <summary>
    /// Entry, Active, ClosedWon or ClosedLost. The widget colours and the open/won split read
    /// this rather than the name, which the tenant owns and may change at any time.
    /// </summary>
    public string StageType { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal TotalValue { get; set; }
    public bool IsTerminal { get; set; }

    public Guid PipelineId { get; set; }
    public string PipelineName { get; set; } = string.Empty;
}

public sealed class DashboardAttention
{
    public int OverdueTaskCount { get; set; }
    public List<OverdueTaskItem> OverdueTasks { get; set; } = [];
    public int StaleLeadCount { get; set; }
    public List<StaleLeadItem> StaleLeads { get; set; } = [];
    public int StaleAfterDays { get; set; }
    public DateTime GeneratedAt { get; set; }
}

public sealed class OverdueTaskItem
{
    public Guid TaskId { get; set; }
    public Guid LeadId { get; set; }
    public string LeadName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public int DaysOverdue { get; set; }
    public string Priority { get; set; } = string.Empty;
}

public sealed class StaleLeadItem
{
    public Guid LeadId { get; set; }
    public string LeadName { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public DateTime LastActivityAt { get; set; }
    public int DaysSinceActivity { get; set; }
}

public sealed class DashboardActivity
{
    public List<ActivityFeedItem> RecentActivity { get; set; } = [];
    public List<RecentConversionItem> RecentConversions { get; set; } = [];
    public int ConversionCount { get; set; }
    public DateTime GeneratedAt { get; set; }
}

public sealed class ActivityFeedItem
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string SubjectType { get; set; } = string.Empty;
    public Guid SubjectId { get; set; }
    public string? ActorName { get; set; }
    public DateTime OccurredAt { get; set; }
}

public sealed class RecentConversionItem
{
    public Guid LeadId { get; set; }
    public string LeadName { get; set; } = string.Empty;
    public Guid? OpportunityId { get; set; }
    public string? OpportunityTitle { get; set; }
    public decimal? Amount { get; set; }
    public string? CurrencyCode { get; set; }
    public DateTime ConvertedAt { get; set; }
}
