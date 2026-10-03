using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public enum RoutingStrategy { RoundRobin = 0, Territory = 1 }
public enum RoutingDimension { LeadSource = 0, CustomField = 1 }

public sealed class RoutingConfig : BaseTenantEntity
{
    private RoutingConfig() { }

    public RoutingStrategy Strategy { get; private set; } = RoutingStrategy.RoundRobin;
    public RoutingDimension Dimension { get; private set; } = RoutingDimension.LeadSource;
    public string? TerritoryMapJson { get; private set; }
    public int RoundRobinPointer { get; set; } = 0;
    /// <summary>
    /// The pipeline this routing config targets, or null for every pipeline in the tenant.
    ///
    /// <b>Precedence: the more specific target wins, and does not merge.</b> When a
    /// pipeline-scoped routing config and a tenant-wide one both apply to the same record, the
    /// pipeline-scoped one is used and the tenant-wide one is not also applied. Unioning
    /// them would make a pipeline-scoped routing config unable to override anything — it could only
    /// ever add — and "scoped to this pipeline" would then be indistinguishable from
    /// "additional".
    ///
    /// Nullable rather than defaulted to the tenant's default pipeline: null means "no
    /// opinion about pipelines", which is what every row that existed before multiple
    /// pipelines did meant, and is what a single-pipeline tenant keeps meaning. A default
    /// value here would have silently narrowed every existing row the day a second pipeline
    /// was created.
    ///
    /// <b>On pipeline removal</b> the reference is set back to null — the routing config reverts to
    /// tenant-wide rather than being deleted or left pointing at a pipeline that is gone.
    /// Deleting it would destroy configuration the Admin never asked to lose; leaving it
    /// dangling would make it silently match nothing, which is the same as deleting it but
    /// without saying so.
    /// </summary>
    public Guid? PipelineId { get; private set; }

    public bool IsEnabled { get; private set; } = true;
    // For Territory + CustomField: which custom field definition drives routing
    public string? CustomFieldKey { get; private set; }

    public static RoutingConfig Create(Guid tenantId, RoutingStrategy strategy, RoutingDimension dimension,
        string? territoryMapJson = null, string? customFieldKey = null, Guid? pipelineId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Strategy = strategy,
            Dimension = dimension,
            TerritoryMapJson = territoryMapJson,
            CustomFieldKey = customFieldKey,
            PipelineId = pipelineId
        };

    public void Update(RoutingStrategy strategy, RoutingDimension dimension,
        string? territoryMapJson, string? customFieldKey)
    {
        Strategy = strategy;
        Dimension = dimension;
        TerritoryMapJson = territoryMapJson;
        CustomFieldKey = customFieldKey;
    }

    /// <summary>Retargets the config. Null makes it tenant-wide again.</summary>
    public void SetPipeline(Guid? pipelineId) => PipelineId = pipelineId;

    public void Disable() => IsEnabled = false;
    public void Enable() => IsEnabled = true;
}
