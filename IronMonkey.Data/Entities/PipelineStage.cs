using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public enum StageType { Entry = 0, Active = 1, ClosedWon = 2, ClosedLost = 3 }

public sealed class PipelineStage : BaseTenantEntity
{
    private PipelineStage() { }

    public string Name { get; private set; } = string.Empty;
    public int Order { get; private set; }
    public bool IsActive { get; private set; } = true;
    public StageType StageType { get; private set; } = StageType.Active;

    public bool IsTerminal => StageType is StageType.ClosedWon or StageType.ClosedLost;

    public static PipelineStage Create(Guid tenantId, string name, int order, StageType stageType = StageType.Active)
        => new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = name, Order = order, StageType = stageType };

    public void Update(string name, int order) { Name = name; Order = order; }

    public void Deactivate() => IsActive = false;

    public void SetStageType(StageType type) => StageType = type;
}
