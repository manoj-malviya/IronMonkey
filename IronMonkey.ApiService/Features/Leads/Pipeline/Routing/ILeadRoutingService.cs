using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Pipeline.Routing;

public interface ILeadRoutingService
{
    /// <summary>
    /// Returns the UserId to assign to the lead, or null if no routing match (D-16: leave unassigned).
    /// Side effect: advances the round-robin pointer if strategy is RoundRobin.
    /// </summary>
    Task<Guid?> GetNextAssigneeAsync(
        Guid tenantId, string connectionString, Lead lead, TenantDbContext db,
        CancellationToken cancellationToken);
}
