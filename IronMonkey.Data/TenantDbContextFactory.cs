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

/// <param name="ambientInterceptors">
/// Interceptors attached to every context this factory creates — activity tracking, in
/// practice. They are resolved from DI rather than passed per call because all 67 call
/// sites used the interceptor-less overload, so nothing was ever tracked despite the
/// interceptor being registered. Optional so tests and design-time tooling can construct
/// the factory directly.
/// </param>
public class TenantDbContextFactory(
    IEnumerable<IInterceptor>? ambientInterceptors = null) : ITenantDbContextFactory
{
    private readonly IInterceptor[] _ambient = ambientInterceptors?.ToArray() ?? [];

    public TenantDbContext CreateForTenant(string connectionString, Guid tenantId)
        => CreateForTenant(connectionString, tenantId, _ambient);

    public TenantDbContext CreateForTenant(string connectionString, Guid tenantId, IEnumerable<IInterceptor> interceptors)
    {
        var builder = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connectionString);

        foreach (var interceptor in interceptors)
            builder.AddInterceptors(interceptor);

        return new TenantDbContext(builder.Options, tenantId);
    }
}
