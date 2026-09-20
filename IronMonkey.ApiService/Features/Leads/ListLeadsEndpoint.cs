using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads;

public class ListLeadsEndpoint : IEndpoint
{
    /// <summary>Rows per page when the caller does not ask for a size.</summary>
    private const int DefaultPageSize = 25;

    /// <summary>
    /// Ceiling on an explicit pageSize. Without it a caller can ask for everything and
    /// turn the list into the unbounded query pagination was added to avoid.
    /// </summary>
    private const int MaxPageSize = 200;

    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/leads", Handle)
        .WithSummary("List leads for the current tenant, paginated, with optional search, stage filter and sort")
        .WithTags("Leads")
        .RequireAuthorization();

    public record LeadItem(
        Guid Id, string FirstName, string LastName, string Email, string Mobile,
        string Source, Guid PipelineStageId, string StageName, bool IsConverted,
        Guid? AssignedToUserId, DateTime CreatedAt,
        Dictionary<string, object?> CustomFields);

    /// <summary>
    /// A page of leads plus the total the filter matches, so the UI can show an honest
    /// "x of y" count and how many pages exist without fetching every row.
    /// </summary>
    public record LeadPage(
        List<LeadItem> Items,
        int TotalCount,
        int Page,
        int PageSize,
        int TotalPages);

    internal static async Task<Ok<LeadPage>> Handle(
        string? search,
        Guid? stageId,
        string? sort,
        int? page,
        int? pageSize,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var query = db.Leads.Include(l => l.Stage).AsQueryable();

        if (stageId.HasValue)
            query = query.Where(l => l.PipelineStageId == stageId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // ILIKE via EF.Functions so the match is case-insensitive in Postgres without
            // pulling every lead into memory.
            var pattern = $"%{search.Trim()}%";
            query = query.Where(l =>
                EF.Functions.ILike(l.FirstName, pattern) ||
                EF.Functions.ILike(l.LastName, pattern) ||
                EF.Functions.ILike(l.Email, pattern) ||
                EF.Functions.ILike(l.Mobile, pattern));
        }

        // Counted before paging, so the total reflects the filter rather than the page.
        var totalCount = await query.CountAsync(cancellationToken);

        query = ApplySort(query, sort);

        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)size);

        // A page number past the end (a deep link after rows were deleted, or a stale
        // filter) is clamped to the last real page rather than returning an empty list
        // that looks like "no results".
        var requested = Math.Max(page ?? 1, 1);
        var current = totalPages == 0 ? 1 : Math.Min(requested, totalPages);

        var leads = await query
            .Skip((current - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        var items = leads.Select(l => new LeadItem(
            l.Id, l.FirstName, l.LastName, l.Email, l.Mobile,
            l.Source.ToString(), l.PipelineStageId, l.Stage?.Name ?? "—",
            l.IsConverted, l.AssignedToUserId, l.CreatedAt, l.CustomFields.Values)).ToList();

        return TypedResults.Ok(new LeadPage(items, totalCount, current, size, totalPages));
    }

    /// <summary>
    /// Maps the caller's sort key to an ordering.
    ///
    /// An allow-list rather than reflection over the supplied string: an arbitrary property
    /// name would let a caller order by (and so probe) columns the list never exposes, and
    /// an untranslatable one would throw at runtime.
    ///
    /// Every branch ends with a tiebreak on Id. Without it, rows sharing a sort value have
    /// no defined order between queries, so a row can appear on two pages or on none.
    /// </summary>
    private static IQueryable<Lead> ApplySort(IQueryable<Lead> query, string? sort) => sort switch
    {
        "name" => query.OrderBy(l => l.FirstName).ThenBy(l => l.LastName).ThenBy(l => l.Id),
        "name_desc" => query.OrderByDescending(l => l.FirstName).ThenByDescending(l => l.LastName).ThenBy(l => l.Id),
        "email" => query.OrderBy(l => l.Email).ThenBy(l => l.Id),
        "email_desc" => query.OrderByDescending(l => l.Email).ThenBy(l => l.Id),
        "stage" => query.OrderBy(l => l.Stage.Order).ThenBy(l => l.Id),
        "stage_desc" => query.OrderByDescending(l => l.Stage.Order).ThenBy(l => l.Id),
        "created" => query.OrderBy(l => l.CreatedAt).ThenBy(l => l.Id),
        _ => query.OrderByDescending(l => l.CreatedAt).ThenBy(l => l.Id),
    };
}
