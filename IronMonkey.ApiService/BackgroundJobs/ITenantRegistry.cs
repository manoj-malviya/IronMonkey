namespace IronMonkey.ApiService.BackgroundJobs;

public interface ITenantRegistry
{
    Task<string> GetConnectionStringAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(Guid TenantId, string ConnectionString)>> GetAllProvisionedTenantsAsync(CancellationToken cancellationToken = default);
}
