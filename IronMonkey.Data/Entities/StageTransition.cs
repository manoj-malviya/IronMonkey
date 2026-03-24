using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public sealed class StageTransition : BaseTenantEntity
{
    private StageTransition() { }

    public Guid FromStageId { get; private set; }
    public Guid ToStageId { get; private set; }

    public PipelineStage FromStage { get; private set; } = null!;
    public PipelineStage ToStage { get; private set; } = null!;

    public static StageTransition Create(Guid tenantId, Guid fromStageId, Guid toStageId)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FromStageId = fromStageId,
            ToStageId = toStageId
        };
}
