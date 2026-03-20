using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Common.Auth;

internal sealed class TenantService : ITenantService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly CentralDbContext _centralDb;

    public TenantService(IHttpContextAccessor httpContextAccessor, CentralDbContext centralDb)
    {
        _httpContextAccessor = httpContextAccessor;
        _centralDb = centralDb;
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

        return tenant.DatabaseConnectionString;
    }
}
