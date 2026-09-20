using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Common.Auth;

internal sealed class TenantService : ITenantService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly CentralDbContext _centralDb;
    private readonly ITenantConnectionStringResolver? _connectionStringResolver;

    /// <param name="connectionStringResolver">
    /// Rebases the stored tenant connection string onto the live central server. Optional so
    /// tests can construct this directly; when absent the stored string is used as-is, which
    /// is correct there because the test fixture's string is already current.
    /// </param>
    public TenantService(
        IHttpContextAccessor httpContextAccessor,
        CentralDbContext centralDb,
        ITenantConnectionStringResolver? connectionStringResolver = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _centralDb = centralDb;
        _connectionStringResolver = connectionStringResolver;
    }

    public Guid GetCurrentTenantId()
    {
        var tenantId = _httpContextAccessor.HttpContext?.User.GetTenantId();
        if (tenantId is null || tenantId == Guid.Empty)
            throw new ApplicationException("Tenant context is unavailable — no tenant_id claim in JWT.");
        return tenantId.Value;
    }

    public async Task<string> GetConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = GetCurrentTenantId();

        var tenant = await _centralDb.Tenants
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant is null)
            throw new ApplicationException($"Tenant {tenantId} not found in registry.");

        if (!tenant.IsProvisioned || tenant.DatabaseConnectionString is null)
            throw new ApplicationException($"Tenant {tenantId} is not yet provisioned.");

        // Rebased onto the live central server: the stored string pins the host/port that
        // existed at provisioning time, which Aspire changes on every restart.
        return _connectionStringResolver is null
            ? tenant.DatabaseConnectionString
            : _connectionStringResolver.Resolve(tenant.DatabaseConnectionString);
    }
}
