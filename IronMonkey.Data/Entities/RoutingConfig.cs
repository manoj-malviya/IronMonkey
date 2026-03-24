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
    public bool IsEnabled { get; private set; } = true;
    // For Territory + CustomField: which custom field definition drives routing
    public string? CustomFieldKey { get; private set; }

    public static RoutingConfig Create(Guid tenantId, RoutingStrategy strategy, RoutingDimension dimension,
        string? territoryMapJson = null, string? customFieldKey = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Strategy = strategy,
            Dimension = dimension,
            TerritoryMapJson = territoryMapJson,
            CustomFieldKey = customFieldKey
        };

    public void Update(RoutingStrategy strategy, RoutingDimension dimension,
        string? territoryMapJson, string? customFieldKey)
    {
        Strategy = strategy;
        Dimension = dimension;
        TerritoryMapJson = territoryMapJson;
        CustomFieldKey = customFieldKey;
    }

    public void Disable() => IsEnabled = false;
    public void Enable() => IsEnabled = true;
}
