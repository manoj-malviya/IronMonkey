using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Merge;

public interface ILeadMergeService
{
    Task<Lead> MergeAsync(Guid tenantId, Guid sourceLeadId, Guid targetLeadId, Guid userId, CancellationToken ct = default);
}
