using Microsoft.EntityFrameworkCore;

namespace IronMonkey.Data;

public interface ITenantDbContextFactory
{
    TenantDbContext CreateForTenant(string connectionString, Guid tenantId);
}

public class TenantDbContextFactory : ITenantDbContextFactory
{
    public TenantDbContext CreateForTenant(string connectionString, Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TenantDbContext(options, tenantId);
    }
}
