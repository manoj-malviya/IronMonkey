using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Pipelines;
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
        string Source, Guid PipelineStageId, string StageName,
        Guid PipelineId, string PipelineName, bool IsConverted,
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
        int TotalPages,
        /// <summary>Which pipeline(s) TotalCount covers — a name, or "All pipelines". A
        /// total shown without this is a number whose meaning the reader has to guess.</summary>
        string ScopeLabel,
        Guid? PipelineId,
        bool IsTenantWide,
        bool IsMultiPipeline);

    /// <param name="pipelineId">
    /// Which pipeline's leads to list. Omitted means the default pipeline, NOT all of them.
    /// TotalCount is counted over the same scope, so "1–25 of 240" describes the pipeline
    /// the list claims to be showing rather than the tenant. Pass "all" for every pipeline.
    /// </param>
    internal static async Task<Results<Ok<LeadPage>, BadRequest<string>>> Handle(
        string? search,
        Guid? stageId,
        string? sort,
        int? page,
        int? pageSize,
        string? pipelineId,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IPipelineScopeResolver scopeResolver,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var scope = await scopeResolver.ResolveAsync(
            db, PipelineRecordType.Lead, pipelineId, cancellationToken);

        if (!scope.IsValid) return TypedResults.BadRequest(scope.Error!);

        var scopedPipelines = scope.PipelineIds;

        // Applied before the count, so the total describes the same set the page comes from.
        // No Include: the only things read from those navigations are two names, and a
        // projection below fetches exactly them. AsNoTracking because nothing here mutates
        // or saves — tracking 25 leads plus their stage and pipeline was pure overhead on a
        // read-only list.
        var query = db.Leads
            .AsNoTracking()
            .Where(l => scopedPipelines.Contains(l.PipelineId))
            .AsQueryable();

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

        // Projected AFTER the sort, so ApplySort keeps ordering by l.Stage.Order (EF emits
        // the join) and needs no change. CustomFields stays in the projection: it is part of
        // the published LeadItem contract, so dropping it would be an API break rather than
        // an optimisation.
        var rows = await query
            .Skip((current - 1) * size)
            .Take(size)
            .Select(l => new Row(
                l.Id, l.FirstName, l.LastName, l.Email, l.Mobile,
                l.Source, l.PipelineStageId, l.Stage.Name,
                l.PipelineId, l.Pipeline.Name,
                l.IsConverted, l.AssignedToUserId, l.CreatedAt, l.CustomFields))
            .ToListAsync(cancellationToken);

        // Source.ToString() and the "—" fallbacks cannot be translated to SQL, so they run
        // here over the 25 rows already fetched.
        var items = rows.Select(r => new LeadItem(
            r.Id, r.FirstName, r.LastName, r.Email, r.Mobile,
            r.Source.ToString(), r.PipelineStageId, r.StageName ?? "—",
            r.PipelineId, r.PipelineName ?? "—",
            r.IsConverted, r.AssignedToUserId, r.CreatedAt, r.CustomFields.Values)).ToList();

        return TypedResults.Ok(new LeadPage(
            items, totalCount, current, size, totalPages,
            scope.ScopeLabel, scope.SelectedPipelineId,
            scope.IsTenantWide, scope.IsMultiPipelineTenant));
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
    /// <summary>
    /// The columns the list actually reads, so the query fetches those and not whole
    /// entities. Private to this endpoint — the published shape is <see cref="LeadItem"/>.
    /// </summary>
    private sealed record Row(
        Guid Id, string FirstName, string LastName, string Email, string Mobile,
        LeadSource Source, Guid PipelineStageId, string? StageName,
        Guid PipelineId, string? PipelineName,
        bool IsConverted, Guid? AssignedToUserId, DateTime CreatedAt,
        CustomFieldValues CustomFields);

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
