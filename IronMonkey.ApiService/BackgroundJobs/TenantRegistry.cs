using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.BackgroundJobs;

public class TenantRegistry : ITenantRegistry
{
    private readonly CentralDbContext _centralDb;
    private readonly ITenantConnectionStringResolver _connectionStringResolver;

    public TenantRegistry(
        CentralDbContext centralDb,
        ITenantConnectionStringResolver connectionStringResolver)
    {
        _centralDb = centralDb;
        _connectionStringResolver = connectionStringResolver;
    }

    public async Task<string> GetConnectionStringAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await _centralDb.Tenants
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == tenantId && t.IsProvisioned, cancellationToken);

        if (tenant?.DatabaseConnectionString is null)
            throw new InvalidOperationException($"Tenant {tenantId} is not provisioned or connection string is null.");

        return _connectionStringResolver.Resolve(tenant.DatabaseConnectionString);
    }

    public async Task<IReadOnlyList<(Guid TenantId, string ConnectionString)>> GetAllProvisionedTenantsAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await _centralDb.Tenants
            .AsNoTracking()
            .Where(t => t.IsProvisioned && t.DatabaseConnectionString != null)
            .Select(t => new { t.Id, t.DatabaseConnectionString })
            .ToListAsync(cancellationToken);

        return tenants
            .Select(t => (t.Id, _connectionStringResolver.Resolve(t.DatabaseConnectionString!)))
            .ToList();
    }
}
