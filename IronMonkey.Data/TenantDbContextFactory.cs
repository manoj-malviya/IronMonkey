using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IronMonkey.Data;

public interface ITenantDbContextFactory
{
    TenantDbContext CreateForTenant(string connectionString, Guid tenantId);

    /// <summary>
    /// Creates a TenantDbContext with additional EF Core interceptors.
    /// Used by API service layer to inject activity tracking interceptors.
    /// </summary>
    TenantDbContext CreateForTenant(string connectionString, Guid tenantId, IEnumerable<IInterceptor> interceptors);
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

    public TenantDbContext CreateForTenant(string connectionString, Guid tenantId, IEnumerable<IInterceptor> interceptors)
    {
        var builder = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connectionString);

        foreach (var interceptor in interceptors)
            builder.AddInterceptors(interceptor);

        return new TenantDbContext(builder.Options, tenantId);
    }
}
