namespace IronMonkey.ApiService.Features.Leads.Workflow.Execution;

/// <summary>
/// Retention and reconciliation settings for workflow execution history, bound from the
/// <c>WorkflowExecutionLogs</c> configuration section.
///
/// History grows with every lead change in every tenant, so it needs a ceiling by default
/// rather than by opt-in: a busy tenant with an hourly time-elapsed scan writes a row per
/// active lead per hour, which is unbounded growth inside the tenant's own database.
/// </summary>
public sealed class WorkflowExecutionLogOptions
{
    public const string SectionName = "WorkflowExecutionLogs";

    /// <summary>
    /// How long a finished execution row is kept. 90 days covers "what happened to this lead
    /// last quarter" — the question the history exists to answer — without keeping rows whose
    /// rule has since been rewritten.
    /// </summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// How long a row may sit in <c>Running</c> before reconciliation marks it
    /// <c>Abandoned</c>.
    ///
    /// Must stay comfortably above the webhook client's timeout plus Hangfire's retry delays,
    /// or a slow-but-working run would be declared abandoned while it is still going. One hour
    /// is well clear of both.
    /// </summary>
    public int StaleRunningThresholdMinutes { get; set; } = 60;

    /// <summary>
    /// Rows deleted per statement during the retention sweep. Batched so a tenant with a large
    /// backlog does not hold one long transaction and a table-wide lock while it is cleaned.
    /// </summary>
    public int CleanupBatchSize { get; set; } = 5_000;

    /// <summary>
    /// Set false to stop the maintenance job from deleting anything — for an operator who
    /// needs history preserved while investigating. Reconciliation of stale rows continues
    /// either way, since leaving a row Running forever is a correctness problem, not a
    /// capacity one.
    /// </summary>
    public bool RetentionEnabled { get; set; } = true;

    /// <summary>Guards against a misconfigured zero or negative value wiping all history.</summary>
    public int EffectiveRetentionDays => RetentionDays < 1 ? 1 : RetentionDays;

    public int EffectiveStaleThresholdMinutes => StaleRunningThresholdMinutes < 1 ? 1 : StaleRunningThresholdMinutes;

    public int EffectiveBatchSize => Math.Clamp(CleanupBatchSize, 100, 50_000);
}
