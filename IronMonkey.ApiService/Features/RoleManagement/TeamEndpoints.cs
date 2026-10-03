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

public sealed record TeamResponse(Guid Id, string Name, string? Description, Guid? ManagerUserId, string? ManagerName,
    Guid? ParentTeamId, List<TeamMemberResponse> Members);

public sealed record TeamMemberResponse(Guid UserId, string Name, string Email);

public sealed record SaveTeamRequest(string Name, string? Description, Guid? ManagerUserId, Guid? ParentTeamId, List<Guid>? MemberUserIds);

/// <summary>
/// Teams and their hierarchy. Reads need users:read; writes need users:write. Every write
/// invalidates the tenant's cached visibility, because Team scope for one user depends on other
/// users' memberships and on who manages what.
/// </summary>
public class TeamEndpoints : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/users/teams", List).WithTags("Teams").WithSummary("List teams with members and managers")
            .RequireAuthorization(PermissionConstants.UsersRead);
        app.MapPost("/users/teams", Create).WithTags("Teams").WithSummary("Create a team")
            .RequireAuthorization(PermissionConstants.UsersWrite);
        app.MapPut("/users/teams/{id:guid}", Update).WithTags("Teams").WithSummary("Update a team, its manager, parent and members")
            .RequireAuthorization(PermissionConstants.UsersWrite);
        app.MapDelete("/users/teams/{id:guid}", Delete).WithTags("Teams").WithSummary("Delete a team with no sub-teams")
            .RequireAuthorization(PermissionConstants.UsersWrite);
    }

    internal static async Task<Ok<List<TeamResponse>>> List(ITenantService tenantService, CancellationToken ct)
    {
        await using var db = await OpenAsync(tenantService, ct);
        return TypedResults.Ok(await ReadAllAsync(db, ct));
    }

    internal static async Task<Results<Created<TeamResponse>, ValidationError>> Create(SaveTeamRequest request,
        ITenantService tenantService, IUserContext userContext, IRecordVisibilityCache visibilityCache, CancellationToken ct)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        await using var db = await OpenAsync(tenantService, ct);

        var team = Team.Create(tenantId, request.Name ?? "", request.Description, request.ManagerUserId);
        if (await ApplyAsync(db, team, request, isNew: true, ct) is { } error) return new ValidationError(error);

        db.Teams.Add(team);
        PermissionAudit.Record(db, tenantId, userContext.UserId, PermissionAudit.TeamSubject, team.Id, "TeamCreated",
            team.Id.ToString(), null, Snapshot(team));
        await db.SaveChangesAsync(ct);
        await visibilityCache.InvalidateTenantAsync(tenantId, ct);

        return TypedResults.Created($"/users/teams/{team.Id}", (await ReadAllAsync(db, ct)).Single(t => t.Id == team.Id));
    }

    internal static async Task<Results<Ok<TeamResponse>, ValidationError, NotFound>> Update(Guid id, SaveTeamRequest request,
        ITenantService tenantService, IUserContext userContext, IRecordVisibilityCache visibilityCache, CancellationToken ct)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        await using var db = await OpenAsync(tenantService, ct);

        var team = await db.Teams.Include(t => t.Members).SingleOrDefaultAsync(t => t.Id == id, ct);
        if (team is null) return TypedResults.NotFound();

        var before = Snapshot(team);
        if (await ApplyAsync(db, team, request, isNew: false, ct) is { } error) return new ValidationError(error);

        PermissionAudit.Record(db, tenantId, userContext.UserId, PermissionAudit.TeamSubject, team.Id, "TeamChanged",
            team.Id.ToString(), before, Snapshot(team));
        await db.SaveChangesAsync(ct);
        await visibilityCache.InvalidateTenantAsync(tenantId, ct);

        return TypedResults.Ok((await ReadAllAsync(db, ct)).Single(t => t.Id == team.Id));
    }

    internal static async Task<Results<NoContent, ValidationError, NotFound>> Delete(Guid id,
        ITenantService tenantService, IUserContext userContext, IRecordVisibilityCache visibilityCache, CancellationToken ct)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        await using var db = await OpenAsync(tenantService, ct);

        var team = await db.Teams.Include(t => t.Members).SingleOrDefaultAsync(t => t.Id == id, ct);
        if (team is null) return TypedResults.NotFound();
        if (await db.Teams.AnyAsync(t => t.ParentTeamId == id, ct))
            return new ValidationError("Move or delete this team's sub-teams first.");

        PermissionAudit.Record(db, tenantId, userContext.UserId, PermissionAudit.TeamSubject, team.Id, "TeamDeleted",
            team.Id.ToString(), Snapshot(team), null);
        team.IsDeleted = true;
        team.DeletedAt = DateTime.UtcNow;
        team.SetMembers([]);
        await db.SaveChangesAsync(ct);
        await visibilityCache.InvalidateTenantAsync(tenantId, ct);
        return TypedResults.NoContent();
    }

    private static async Task<string?> ApplyAsync(TenantDbContext db, Team team, SaveTeamRequest request, bool isNew, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return "A team name is required.";
        if (request.Name.Trim().Length > 100) return "Team names are at most 100 characters.";

        var name = request.Name.Trim();
        if (await db.Teams.AnyAsync(t => t.Id != team.Id && t.Name.ToLower() == name.ToLower(), ct))
            return $"A team named '{name}' already exists.";

        var userIds = (request.MemberUserIds ?? []).Distinct().ToList();
        if (request.ManagerUserId is { } manager) userIds.Add(manager);
        var known = await db.Users.Where(u => userIds.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
        if (userIds.Distinct().Any(u => !known.Contains(u))) return "One or more selected users do not exist.";

        // Cycles and depth are rejected here, at write time. A cycle in the tree would make
        // every visibility resolution for this tenant walk it forever.
        var parents = await db.Teams.Where(t => t.Id != team.Id).ToDictionaryAsync(t => t.Id, t => t.ParentTeamId, ct);
        parents[team.Id] = team.ParentTeamId;
        if (TeamHierarchy.ValidateParent(team.Id, request.ParentTeamId, parents) is { } hierarchyError) return hierarchyError;

        team.Update(name, request.Description, request.ManagerUserId);
        team.SetParent(request.ParentTeamId);
        if (request.MemberUserIds is not null || isNew) team.SetMembers(request.MemberUserIds ?? []);
        return null;
    }

    private static Dictionary<string, object?> Snapshot(Team t) => new()
    {
        ["Name"] = t.Name,
        ["ManagerUserId"] = t.ManagerUserId,
        ["ParentTeamId"] = t.ParentTeamId,
        ["Members"] = t.Members.Select(m => m.UserId).Order().ToList()
    };

    private static async Task<List<TeamResponse>> ReadAllAsync(TenantDbContext db, CancellationToken ct)
    {
        var teams = await db.Teams.AsNoTracking().Include(t => t.Members).OrderBy(t => t.Name).ThenBy(t => t.Id).ToListAsync(ct);
        var ids = teams.SelectMany(t => t.Members.Select(m => m.UserId).Append(t.ManagerUserId ?? Guid.Empty)).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

        return teams.Select(t => new TeamResponse(t.Id, t.Name, t.Description, t.ManagerUserId,
            t.ManagerUserId is { } m && users.TryGetValue(m, out var mu) ? mu.Name : null, t.ParentTeamId,
            t.Members.Where(x => users.ContainsKey(x.UserId))
                .Select(x => new TeamMemberResponse(x.UserId, users[x.UserId].Name, users[x.UserId].Email))
                .OrderBy(x => x.Name).ToList())).ToList();
    }

    /// <summary>Team administration always sees the whole tenant's teams and users.</summary>
    private static async Task<TenantDbContext> OpenAsync(ITenantService tenantService, CancellationToken ct) =>
        new(new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(await tenantService.GetConnectionStringAsync(ct)).Options,
            tenantService.GetCurrentTenantId(), RecordVisibility.Unrestricted);
}
