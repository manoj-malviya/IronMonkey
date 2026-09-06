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

    public AuthorizationService(
        CentralDbContext centralDb,
        ITenantDbContextFactory tenantContextFactory,
        ICacheService cacheService)
    {
        _centralDb = centralDb;
        _tenantContextFactory = tenantContextFactory;
        _cacheService = cacheService;
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

        await using var tenantDb = _tenantContextFactory.CreateForTenant(
            tenant.DatabaseConnectionString, tenantId);

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
