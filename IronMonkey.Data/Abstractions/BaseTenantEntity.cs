namespace IronMonkey.Data.Abstractions;

/// <summary>
/// Base class for entities that belong to a specific tenant/organization
/// </summary>
public abstract class BaseTenantEntity : Entity
{
    protected BaseTenantEntity(Guid id, Guid tenantId)
        : base(id)
    {
        TenantId = tenantId;
    }

    protected BaseTenantEntity()
    {
    }

    public Guid TenantId { get; init; }
}
