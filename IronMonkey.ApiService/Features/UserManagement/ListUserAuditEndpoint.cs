using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.UserManagement;

/// <summary>
/// The team-administration audit trail: who changed whose role, who was deactivated, who
/// invited whom.
///
/// Tenant-scoped by the global query filter like everything else in this database, so one
/// tenant can never read another's administration history.
/// </summary>
public class ListUserAuditEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/users/audit", Handle)
        .WithSummary("List team administration audit events")
        .WithTags("User Management")
        .RequireAuthorization(PermissionConstants.UsersRead);

    public record AuditItem(
        Guid Id,
        string EventType,
        Guid? TargetUserId,
        string TargetEmail,
        string? ActorName,
        string? Detail,
        DateTime OccurredAt);

    internal static async Task<Ok<List<AuditItem>>> Handle(
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        Guid? userId,
        int? limit,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        // Clamped rather than trusted: an unbounded limit from the query string would let a
        // caller pull the whole trail in one request.
        var take = Math.Clamp(limit ?? 100, 1, 500);

        var query = db.UserAuditLogs.AsNoTracking();

        if (userId is { } target)
            query = query.Where(a => a.TargetUserId == target);

        var rows = await query
            // Tiebreak on Id: rows sharing a timestamp otherwise have no defined order
            // between queries, so one can appear twice or not at all across pages.
            .OrderByDescending(a => a.OccurredAt)
            .ThenBy(a => a.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        // Resolved separately rather than through a navigation: there is deliberately no FK
        // from the audit row to User, so the trail survives the row it describes.
        var actorIds = rows.Where(r => r.ActorUserId.HasValue).Select(r => r.ActorUserId!.Value).Distinct().ToList();

        var actorNames = await db.Users
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && actorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var items = rows.Select(a => new AuditItem(
            a.Id,
            a.EventType,
            a.TargetUserId,
            a.TargetEmail,
            a.ActorUserId is { } actor && actorNames.TryGetValue(actor, out var n) ? n : null,
            a.Detail,
            a.OccurredAt)).ToList();

        return TypedResults.Ok(items);
    }
}
