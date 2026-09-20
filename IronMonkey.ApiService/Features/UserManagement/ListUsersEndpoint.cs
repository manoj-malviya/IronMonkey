using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.UserManagement;

/// <summary>
/// The tenant's members, searchable, filterable and paged.
///
/// Returns a PAGE OBJECT, not a bare array, matching GET /api/leads: the total is counted
/// before paging so "1–25 of 240" is honest, and a page past the end clamps to the last real
/// page rather than returning an empty list that reads as "no results".
/// </summary>
public class ListUsersEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/users", Handle)
        .WithSummary("List, search and filter users in the current tenant")
        .RequireAuthorization(PermissionConstants.UsersRead);

    public record UserItem(Guid Id, string Name, string Email, int RoleId, string RoleName, bool IsActive, DateTime CreatedAt);

    public record PagedUsers(List<UserItem> Items, int TotalCount, int Page, int PageSize, int TotalPages);

    internal static async Task<Ok<PagedUsers>> Handle(
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        string? search,
        int? roleId,
        // bool?, never bool: a non-nullable bool query parameter is REQUIRED in minimal APIs,
        // so declaring it bool would 500 every caller that omits it — including the page's
        // own first load.
        bool? isActive,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var size = Math.Clamp(pageSize ?? 25, 1, 200);

        // IgnoreQueryFilters so deactivated users remain listable; the tenant predicate is
        // then stated explicitly, because dropping the filter drops the tenant scope with it.
        var query = db.Users
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId);

        if (isActive is { } active)
            query = query.Where(u => u.IsDeleted != active);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u =>
                EF.Functions.ILike(u.Name, $"%{term}%") ||
                EF.Functions.ILike(u.Email, $"%{term}%"));
        }

        if (roleId is { } rid)
        {
            // Filtered through Role.Users, the real mapped navigation. User.Roles is a
            // computed property (=> _roles.ToList()) that EF cannot translate, so the
            // obvious u.Roles.Any(...) spelling throws at runtime.
            var roleUserIds = db.Roles
                .Where(r => r.Id == rid)
                .SelectMany(r => r.Users)
                .Select(u => u.Id);

            query = query.Where(u => roleUserIds.Contains(u.Id));
        }

        // Counted BEFORE paging, so the total describes the whole filtered set.
        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)size);

        // Clamped to a real page: a page past the end returns the last one rather than an
        // empty list, which a user reads as "no results" when there plainly are some.
        var current = Math.Clamp(page ?? 1, 1, totalPages);

        var users = await query
            // Tiebreak on Id. Without it, rows sharing a CreatedAt have no defined order
            // between queries, so one can appear on two pages or on none.
            .OrderBy(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Skip((current - 1) * size)
            .Take(size)
            .Include(u => u.Roles)
            .ToListAsync(cancellationToken);

        var items = users.Select(u =>
        {
            // SuperAdmin is platform-only and must never be surfaced in a tenant list.
            var role = u.Roles.FirstOrDefault(r => TenantRoleRules.IsVisibleToTenant(r.Id));
            return new UserItem(
                u.Id,
                u.Name,
                u.Email,
                role?.Id ?? 0,
                role?.Name ?? "None",
                !u.IsDeleted,
                u.CreatedAt);
        }).ToList();

        return TypedResults.Ok(new PagedUsers(items, totalCount, current, size, totalPages));
    }
}
