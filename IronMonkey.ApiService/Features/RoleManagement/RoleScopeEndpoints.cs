using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Visibility;

namespace IronMonkey.ApiService.Features.RoleManagement;

/// <summary>Scope per record type, by name: <c>{"Lead":"Own","Contact":"All",...}</c>.</summary>
public sealed record RoleScopesResponse(int RoleId, Dictionary<string, string> Scopes);
public sealed record SetRoleScopesRequest(Dictionary<string, string> Scopes);

public class GetRoleScopesEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/users/roles/{roleId:int}/scopes", Handle)
        .WithSummary("Record visibility per record type for a role (Own, Team or All)")
        .WithTags("Role Management")
        .RequireAuthorization(PermissionConstants.UsersRead);

    internal static async Task<Results<Ok<RoleScopesResponse>, NotFound>> Handle(
        int roleId, ITenantService tenantService, ITenantDbContextFactory dbContextFactory, CancellationToken ct)
    {
        if (!TenantRoleRules.IsVisibleToTenant(roleId)) return TypedResults.NotFound();

        var tenantId = tenantService.GetCurrentTenantId();
        await using var db = dbContextFactory.CreateForTenant(await tenantService.GetConnectionStringAsync(ct), tenantId);
        if (!await db.Roles.AnyAsync(r => r.Id == roleId, ct)) return TypedResults.NotFound();

        return TypedResults.Ok(await RoleScopes.ReadAsync(db, roleId, ct));
    }
}

public class SetRoleScopesEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/users/roles/{roleId:int}/scopes", Handle)
        .WithSummary("Set a role's record visibility per record type")
        .WithTags("Role Management")
        .RequireAuthorization(PermissionConstants.UsersWrite);

    internal static async Task<Results<Ok<RoleScopesResponse>, ValidationError, NotFound>> Handle(
        int roleId, SetRoleScopesRequest request, ITenantService tenantService, ITenantDbContextFactory dbContextFactory,
        IUserContext userContext, IRecordVisibilityCache visibilityCache, CancellationToken ct)
    {
        if (!TenantRoleRules.IsVisibleToTenant(roleId)) return TypedResults.NotFound();

        var parsed = new Dictionary<VisibilityRecordType, VisibilityScope>();
        foreach (var (typeName, scopeName) in request.Scopes)
        {
            if (!Enum.TryParse<VisibilityRecordType>(typeName, true, out var type) || !Enum.IsDefined(type))
                return new ValidationError($"'{typeName}' is not a record type. Use: {string.Join(", ", Enum.GetNames<VisibilityRecordType>())}.");
            if (!Enum.TryParse<VisibilityScope>(scopeName, true, out var scope) || !Enum.IsDefined(scope))
                return new ValidationError($"'{scopeName}' is not a scope. Use: Own, Team or All.");
            parsed[type] = scope;
        }

        var tenantId = tenantService.GetCurrentTenantId();
        // Unrestricted: managing roles must see every role and scope row, whatever the actor's
        // own record visibility is.
        await using var db = new TenantDbContext(
            new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(await tenantService.GetConnectionStringAsync(ct)).Options,
            tenantId, RecordVisibility.Unrestricted);

        if (!await db.Roles.AnyAsync(r => r.Id == roleId, ct)) return TypedResults.NotFound();

        // A role may be given at most the scope the actor has themselves. Otherwise a manager
        // with Team scope and users:write could widen their own role to All.
        var actorScopes = await RecordVisibilityResolver.ResolveScopesAsync(db, userContext.UserId, ct);
        var tooBroad = parsed.Where(kv => kv.Value > actorScopes[kv.Key]).Select(kv => kv.Key.ToString()).ToList();
        if (tooBroad.Count > 0)
            return new ValidationError($"You cannot grant a wider scope than your own for: {string.Join(", ", tooBroad)}.");

        var before = await RoleScopes.ReadAsync(db, roleId, ct);
        var rows = await db.RoleRecordScopes.Where(s => s.RoleId == roleId).ToListAsync(ct);

        foreach (var (type, scope) in parsed)
        {
            var row = rows.FirstOrDefault(r => r.RecordType == type);
            // All is stored as the absence of a row — the same state every role started in.
            if (scope == VisibilityScope.All)
            {
                if (row is not null) db.RoleRecordScopes.Remove(row);
            }
            else if (row is null) db.RoleRecordScopes.Add(RoleRecordScope.Create(roleId, type, scope));
            else row.Set(scope);
        }

        var after = parsed.Aggregate(new Dictionary<string, string>(before.Scopes),
            (acc, kv) => { acc[kv.Key.ToString()] = kv.Value.ToString(); return acc; });

        PermissionAudit.Record(db, tenantId, userContext.UserId, PermissionAudit.RoleSubject,
            PermissionAudit.RoleSubjectId(roleId), "RoleScopesChanged", roleId.ToString(),
            before.Scopes.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
            after.ToDictionary(kv => kv.Key, kv => (object?)kv.Value));

        await db.SaveChangesAsync(ct);
        await visibilityCache.InvalidateTenantAsync(tenantId, ct);

        return TypedResults.Ok(await RoleScopes.ReadAsync(db, roleId, ct));
    }
}

internal static class RoleScopes
{
    public static async Task<RoleScopesResponse> ReadAsync(TenantDbContext db, int roleId, CancellationToken ct)
    {
        var rows = await db.RoleRecordScopes.AsNoTracking().Where(s => s.RoleId == roleId).ToListAsync(ct);
        return new RoleScopesResponse(roleId, Enum.GetValues<VisibilityRecordType>().ToDictionary(
            t => t.ToString(),
            t => (rows.FirstOrDefault(r => r.RecordType == t)?.Scope ?? VisibilityScope.All).ToString()));
    }
}
