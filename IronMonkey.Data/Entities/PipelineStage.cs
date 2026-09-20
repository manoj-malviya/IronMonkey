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

    /// <summary>
    /// Moves the stage to a new position. Separate from <see cref="Update"/> so a bulk
    /// reorder does not have to restate the name it is not changing.
    /// </summary>
    public void SetOrder(int order) => Order = order;

    public void Rename(string name) => Name = name;

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    /// <summary>Sets active state directly, for a form that edits it as a toggle.</summary>
    public void SetActive(bool isActive) => IsActive = isActive;

    public void SetStageType(StageType type) => StageType = type;
}
