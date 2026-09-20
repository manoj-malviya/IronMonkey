using IronMonkey.ApiService.Common.Cache;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IronMonkey.ApiService.Common.Auth;

/// <summary>
/// Resolves the effective permission set for the current caller.
///
/// Two distinct kinds of caller exist, and they live in different databases:
/// a platform operator (<see cref="PlatformUser"/>, central DB, no tenant) whose grants
/// come from <see cref="PermissionConstants.ForPlatformRole"/>; and a tenant member
/// (<see cref="User"/>, tenant DB) whose grants come from that tenant's role_permissions
/// rows. The tenant_id claim decides which path applies.
/// </summary>
internal sealed class AuthorizationService
{
    private readonly CentralDbContext _centralDb;
    private readonly ITenantDbContextFactory _tenantContextFactory;
    private readonly ICacheService _cacheService;
    private readonly ITenantConnectionStringResolver? _connectionStringResolver;

    /// <param name="connectionStringResolver">
    /// Rebases the stored tenant connection string onto the live central server. Optional so
    /// tests can construct this directly; when absent the stored string is used as-is, which
    /// is correct there because the test fixture's string is already current.
    /// </param>
    public AuthorizationService(
        CentralDbContext centralDb,
        ITenantDbContextFactory tenantContextFactory,
        ICacheService cacheService,
        ITenantConnectionStringResolver? connectionStringResolver = null)
    {
        _centralDb = centralDb;
        _tenantContextFactory = tenantContextFactory;
        _cacheService = cacheService;
        _connectionStringResolver = connectionStringResolver;
    }

    public async Task<HashSet<string>> GetPermissionsForUserAsync(string identityId, Guid tenantId)
    {
        string cacheKey = $"auth:permissions-{tenantId}-{identityId}";
        HashSet<string>? cachedPermissions = await _cacheService.GetAsync<HashSet<string>>(cacheKey);

        if (cachedPermissions is not null)
        {
            return cachedPermissions;
        }

        var permissions = tenantId == Guid.Empty
            ? await GetPlatformPermissionsAsync(identityId)
            : await GetTenantPermissionsAsync(identityId, tenantId);

        await _cacheService.SetAsync(cacheKey, permissions, TimeSpan.FromMinutes(5));

        return permissions;
    }

    /// <summary>
    /// Drops a user's cached permission set so the next request re-resolves it from
    /// role_permissions.
    ///
    /// Without this a role change would not take effect for up to the five minutes the
    /// cache above holds, which is the opposite of what an administrator pressing "save"
    /// expects — and, for a demotion, leaves elevated permissions live after they were
    /// deliberately taken away. The key is built here, next to the one it must match: a
    /// second copy of that format string at the call site would silently stop matching
    /// the moment either changed.
    /// </summary>
    public Task InvalidatePermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
        => _cacheService.RemoveAsync($"auth:permissions-{tenantId}-{userId}", cancellationToken);

    private async Task<HashSet<string>> GetPlatformPermissionsAsync(string identityId)
    {
        // A platform token carries the PlatformUser's Id as its identity.
        if (!Guid.TryParse(identityId, out var platformUserId))
            return [];

        var role = await _centralDb.PlatformUsers
            .AsNoTracking()
            .Where(u => u.Id == platformUserId)
            .Select(u => u.Role)
            .SingleOrDefaultAsync();

        return role is null ? [] : PermissionConstants.ForPlatformRole(role).ToHashSet();
    }

    private async Task<HashSet<string>> GetTenantPermissionsAsync(string identityId, Guid tenantId)
    {
        // Tenant tokens carry the user's primary key as their identity (see LoginEndpoint).
        if (!Guid.TryParse(identityId, out var userId))
            return [];

        var tenant = await _centralDb.Tenants
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == tenantId);

        if (tenant is null || !tenant.IsProvisioned || tenant.DatabaseConnectionString is null)
            return [];

        var connectionString = _connectionStringResolver is null
            ? tenant.DatabaseConnectionString
            : _connectionStringResolver.Resolve(tenant.DatabaseConnectionString);

        await using var tenantDb = _tenantContextFactory.CreateForTenant(connectionString, tenantId);

        // Union across every role the user holds. Selecting the first role's
        // permissions would silently drop grants from any additional role.
        var names = await tenantDb.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .SelectMany(u => u.Roles)
            .SelectMany(r => r.Permissions)
            .Select(p => p.Name)
            .Distinct()
            .ToListAsync();

        return names.ToHashSet();
    }
}
