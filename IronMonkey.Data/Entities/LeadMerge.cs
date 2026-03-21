using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public sealed class LeadMerge : BaseTenantEntity
{
    private LeadMerge() { }

    public Guid SourceLeadId { get; private set; }
    public Guid TargetLeadId { get; private set; }
    public DateTime MergedAt { get; private set; }
    public Guid MergedByUserId { get; private set; }
    public string SourceSnapshot { get; private set; } = string.Empty;
    public string TargetSnapshot { get; private set; } = string.Empty;

    public static LeadMerge Create(Guid tenantId, Guid sourceLeadId, Guid targetLeadId, Guid userId,
        string sourceSnapshot, string targetSnapshot)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceLeadId = sourceLeadId,
            TargetLeadId = targetLeadId,
            MergedAt = DateTime.UtcNow,
            MergedByUserId = userId,
            SourceSnapshot = sourceSnapshot,
            TargetSnapshot = targetSnapshot
        };
}
