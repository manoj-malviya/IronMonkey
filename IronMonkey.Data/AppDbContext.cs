namespace IronMonkey.Data;

/// <summary>
/// Compatibility shim — points to CentralDbContext.
/// All new code should use CentralDbContext or TenantDbContext directly.
/// This will be removed in a later cleanup plan.
/// </summary>
[Obsolete("Use CentralDbContext for central DB operations or TenantDbContext for tenant DB operations.")]
public class AppDbContext : CentralDbContext
{
    public AppDbContext(Microsoft.EntityFrameworkCore.DbContextOptions<CentralDbContext> options)
        : base(options) { }
}
