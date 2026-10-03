using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common.Cache;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Visibility;

namespace IronMonkey.ApiService.Common.Auth;

/// <summary>
/// Works out what one tenant user may see.
///
/// <para><b>Scope per record type</b> is the broadest any of the user's roles grants. A role
/// with no <see cref="RoleRecordScope"/> row for a type grants <see cref="VisibilityScope.All"/>
/// — the pre-scope behaviour — so a tenant that has narrowed nothing resolves to
/// <see cref="RecordVisibility.Unrestricted"/>, exactly as before.</para>
///
/// <para><b>Team</b> resolves to: the user; members of every team the user belongs to; and,
/// for each team the user manages, that team and every sub-team down to
/// <see cref="TeamHierarchy.MaxDepth"/> levels — their members and their managers. The walk
/// is bounded and cycle-safe, so a malformed tree cannot hang a request.</para>
/// </summary>
public static class RecordVisibilityResolver
{
    /// <summary>The user's effective scope per record type: the broadest any of their roles grants.</summary>
    public static async Task<Dictionary<VisibilityRecordType, VisibilityScope>> ResolveScopesAsync(
        TenantDbContext db, Guid userId, CancellationToken ct)
    {
        // Query from Roles: User.Roles is a computed property and cannot be translated.
        var roleIds = await db.Roles
            .Where(r => r.Users.Any(u => u.Id == userId))
            .Select(r => r.Id)
            .ToListAsync(ct);

        var rows = roleIds.Count == 0
            ? []
            : await db.RoleRecordScopes.Where(s => roleIds.Contains(s.RoleId)).ToListAsync(ct);

        var scopes = new Dictionary<VisibilityRecordType, VisibilityScope>();
        foreach (var type in Enum.GetValues<VisibilityRecordType>())
        {
            // Broadest across roles; a role with no row for this type is All. A user with no
            // role at all holds no permissions, so their scope is moot — All keeps it simple.
            scopes[type] = roleIds.Count == 0
                ? VisibilityScope.All
                : roleIds
                    .Select(roleId => rows.FirstOrDefault(r => r.RoleId == roleId && r.RecordType == type)?.Scope ?? VisibilityScope.All)
                    .Max();
        }

        return scopes;
    }

    public static async Task<RecordVisibility> ResolveAsync(TenantDbContext db, Guid userId, CancellationToken ct)
    {
        var scopes = await ResolveScopesAsync(db, userId, ct);

        if (scopes.Values.All(s => s == VisibilityScope.All)) return RecordVisibility.Unrestricted;

        IReadOnlyCollection<Guid>? teamSet = null;
        if (scopes.Values.Contains(VisibilityScope.Team))
            teamSet = await TeamOwnersAsync(db, userId, ct);

        var map = new Dictionary<VisibilityRecordType, IReadOnlyCollection<Guid>?>();
        foreach (var (type, scope) in scopes)
        {
            map[type] = scope switch
            {
                VisibilityScope.All => null,
                VisibilityScope.Team => teamSet,
                _ => [userId]
            };
        }

        return RecordVisibility.Create(map);
    }

    internal static async Task<IReadOnlyCollection<Guid>> TeamOwnersAsync(TenantDbContext db, Guid userId, CancellationToken ct)
    {
        var owners = new HashSet<Guid> { userId };

        var memberOf = await db.TeamMemberships.Where(m => m.UserId == userId).Select(m => m.TeamId).ToListAsync(ct);
        var managed = await db.Teams.Where(t => t.ManagerUserId == userId).Select(t => t.Id).ToListAsync(ct);

        var parents = await db.Teams.Select(t => new { t.Id, t.ParentTeamId }).ToDictionaryAsync(t => t.Id, t => t.ParentTeamId, ct);
        var reach = TeamHierarchy.Descendants(managed, parents);
        reach.UnionWith(memberOf);

        if (reach.Count == 0) return owners;

        owners.UnionWith(await db.TeamMemberships.Where(m => reach.Contains(m.TeamId)).Select(m => m.UserId).ToListAsync(ct));
        owners.UnionWith(await db.Teams.Where(t => reach.Contains(t.Id) && t.ManagerUserId != null)
            .Select(t => t.ManagerUserId!.Value).ToListAsync(ct));

        return owners;
    }
}

/// <summary>Cached per user, invalidated tenant-wide by a version stamp.</summary>
public interface IRecordVisibilityCache
{
    Task<RecordVisibility> GetAsync(Guid tenantId, Guid userId, Func<Task<RecordVisibility>> resolve, CancellationToken ct);

    /// <summary>
    /// Invalidates every user's cached visibility in a tenant. Called on any role scope,
    /// role membership or team change — visibility depends on other users' memberships too,
    /// so dropping one user's entry is not enough.
    /// </summary>
    Task InvalidateTenantAsync(Guid tenantId, CancellationToken ct = default);
}

internal sealed class RecordVisibilityCache(ICacheService cache) : IRecordVisibilityCache
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public async Task<RecordVisibility> GetAsync(Guid tenantId, Guid userId, Func<Task<RecordVisibility>> resolve, CancellationToken ct)
    {
        var version = await cache.GetAsync<string>(VersionKey(tenantId), ct) ?? "0";
        var key = $"auth:visibility-{tenantId}-{version}-{userId}";

        var cached = await cache.GetAsync<CachedVisibility>(key, ct);
        if (cached is not null) return cached.ToVisibility();

        var visibility = await resolve();
        await cache.SetAsync(key, CachedVisibility.From(visibility), Lifetime, ct);
        return visibility;
    }

    public Task InvalidateTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        cache.SetAsync(VersionKey(tenantId), Guid.NewGuid().ToString("N"), TimeSpan.FromDays(1), ct);

    private static string VersionKey(Guid tenantId) => $"auth:visibility-version-{tenantId}";

    /// <summary>Serialisable form; RecordVisibility itself is immutable with private state.</summary>
    internal sealed class CachedVisibility
    {
        public Dictionary<VisibilityRecordType, List<Guid>?> Owners { get; set; } = [];

        public static CachedVisibility From(RecordVisibility v) => new()
        {
            Owners = Enum.GetValues<VisibilityRecordType>().ToDictionary(
                t => t, t => v.IsUnrestricted(t) ? null : v.OwnersFor(t).ToList())
        };

        public RecordVisibility ToVisibility() =>
            RecordVisibility.Create(Owners.ToDictionary(kv => kv.Key, kv => (IReadOnlyCollection<Guid>?)kv.Value));
    }
}

/// <summary>
/// Establishes the caller's record visibility for the rest of the request. Every
/// TenantDbContext created downstream captures it into its query filters.
///
/// <para>Resolved on each request (behind a five-minute cache that every scope, role or team
/// change invalidates), so a narrowed scope applies from the caller's next API request. The
/// Blazor circuit holds only a token; it reads data exclusively through the API, so a long-lived
/// circuit cannot keep showing records a revocation took away — its next fetch is filtered.</para>
///
/// <para>Platform users and anonymous requests get no restriction here: platform users reach
/// tenant data only by impersonation, whose token is the tenant Admin's and resolves normally.</para>
/// </summary>
internal sealed class RecordVisibilityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IRecordVisibilityCache cache, ITenantService tenantService,
        ITenantDbContextFactory factory)
    {
        var user = context.User;
        var tenantId = user.Identity?.IsAuthenticated == true ? user.GetTenantId() : null;

        if (tenantId is null || tenantId == Guid.Empty || !TryGetUserId(user, out var userId))
        {
            await next(context);
            return;
        }

        var visibility = await cache.GetAsync(tenantId.Value, userId, async () =>
        {
            var connectionString = await tenantService.GetConnectionStringAsync(context.RequestAborted);
            // Resolution itself must see every team and role, never a filtered view.
            await using var db = new TenantDbContext(
                new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connectionString).Options,
                tenantId.Value, RecordVisibility.Unrestricted);
            return await RecordVisibilityResolver.ResolveAsync(db, userId, context.RequestAborted);
        }, context.RequestAborted);

        using (RecordVisibility.Enter(visibility))
            await next(context);
    }

    private static bool TryGetUserId(System.Security.Claims.ClaimsPrincipal user, out Guid userId)
    {
        try { userId = user.GetUserId(); return true; }
        catch (ApplicationException) { userId = Guid.Empty; return false; }
    }
}
