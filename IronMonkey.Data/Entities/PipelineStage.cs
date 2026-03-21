using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public sealed class PipelineStage : BaseTenantEntity
{
    private PipelineStage() { }

    public string Name { get; private set; } = string.Empty;
    public int Order { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static PipelineStage Create(Guid tenantId, string name, int order)
        => new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = name, Order = order };

    public void Update(string name, int order) { Name = name; Order = order; }

    public void Deactivate() => IsActive = false;
}
