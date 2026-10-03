using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public enum WorkflowTrigger { FieldChange = 0, StatusChange = 1, TimeElapsed = 2 }

public sealed class WorkflowRule : BaseTenantEntity
{
    private WorkflowRule() { }

    public string Name { get; private set; } = string.Empty;
    public WorkflowTrigger Trigger { get; private set; }
    public string ConditionJson { get; private set; } = "{}";
    public string ActionJson { get; private set; } = "{}";
    /// <summary>
    /// The pipeline this rule targets, or null for every pipeline in the tenant.
    ///
    /// <b>Precedence: the more specific target wins, and does not merge.</b> When a
    /// pipeline-scoped rule and a tenant-wide one both apply to the same record, the
    /// pipeline-scoped one is used and the tenant-wide one is not also applied. Unioning
    /// them would make a pipeline-scoped rule unable to override anything — it could only
    /// ever add — and "scoped to this pipeline" would then be indistinguishable from
    /// "additional".
    ///
    /// Nullable rather than defaulted to the tenant's default pipeline: null means "no
    /// opinion about pipelines", which is what every row that existed before multiple
    /// pipelines did meant, and is what a single-pipeline tenant keeps meaning. A default
    /// value here would have silently narrowed every existing row the day a second pipeline
    /// was created.
    ///
    /// <b>On pipeline removal</b> the reference is set back to null — the rule reverts to
    /// tenant-wide rather than being deleted or left pointing at a pipeline that is gone.
    /// Deleting it would destroy configuration the Admin never asked to lose; leaving it
    /// dangling would make it silently match nothing, which is the same as deleting it but
    /// without saying so.
    /// </summary>
    public Guid? PipelineId { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static WorkflowRule Create(Guid tenantId, string name, WorkflowTrigger trigger,
        string conditionJson, string actionJson, Guid? pipelineId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Trigger = trigger,
            ConditionJson = conditionJson,
            ActionJson = actionJson,
            PipelineId = pipelineId
        };

    public void Update(string name, WorkflowTrigger trigger, string conditionJson, string actionJson)
    {
        Name = name;
        Trigger = trigger;
        ConditionJson = conditionJson;
        ActionJson = actionJson;
    }

    /// <summary>
    /// Retargets the rule. Null makes it tenant-wide again, which is what the pipeline
    /// delete endpoint does to every rule that targeted the removed pipeline.
    /// </summary>
    public void SetPipeline(Guid? pipelineId) => PipelineId = pipelineId;

    public void SetActive(bool isActive) => IsActive = isActive;
}
