using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;

namespace IronMonkey.ApiService.BackgroundJobs;

public class TenantRegistry : ITenantRegistry
{
    private readonly CentralDbContext _centralDb;

    public TenantRegistry(CentralDbContext centralDb)
    {
        _centralDb = centralDb;
    }

    public async Task<string> GetConnectionStringAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await _centralDb.Tenants
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == tenantId && t.IsProvisioned, cancellationToken);

        if (tenant?.DatabaseConnectionString is null)
            throw new InvalidOperationException($"Tenant {tenantId} is not provisioned or connection string is null.");

        return tenant.DatabaseConnectionString;
    }

    public async Task<IReadOnlyList<(Guid TenantId, string ConnectionString)>> GetAllProvisionedTenantsAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await _centralDb.Tenants
            .AsNoTracking()
            .Where(t => t.IsProvisioned && t.DatabaseConnectionString != null)
            .Select(t => new { t.Id, t.DatabaseConnectionString })
            .ToListAsync(cancellationToken);

        return tenants
            .Select(t => (t.Id, t.DatabaseConnectionString!))
            .ToList();
    }
}
