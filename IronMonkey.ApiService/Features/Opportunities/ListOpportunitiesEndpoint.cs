using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Pipelines;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Opportunities;

public class ListOpportunitiesEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/opportunities", Handle)
        .WithSummary("List opportunities for the current tenant with optional search and stage filter")
        .WithTags("Opportunities")
        .RequireAuthorization();

    /// <param name="StageType">
    /// The stage's type as a string. The client decides open/won/lost from this and from
    /// <paramref name="IsTerminal"/>, never by comparing the name to "Won" or "Lost" — the
    /// tenant owns those names and may change them at any time.
    /// </param>
    public record OpportunityItem(
        Guid Id, string Title, Guid ContactId, string ContactName,
        Guid StageId, string Stage, string StageType, bool IsTerminal, int StageOrder,
        Guid PipelineId, string PipelineName,
        decimal Amount, DateTime ExpectedCloseDate,
        string? LossReason, DateTime CreatedAt);

    /// <param name="ScopeLabel">
    /// What the returned set covers — a pipeline's name, or "All pipelines". Echoed so a UI
    /// never presents a list under a heading that does not match what it actually contains.
    /// </param>
    /// <param name="IsMultiPipeline">
    /// Whether this tenant has more than one opportunity pipeline. False means the UI shows
    /// no pipeline picker at all, which is how a single-pipeline tenant stays unchanged.
    /// </param>
    public record OpportunityList(
        List<OpportunityItem> Items,
        Guid? PipelineId,
        string ScopeLabel,
        bool IsTenantWide,
        bool IsMultiPipeline);

    /// <param name="stageId">Filter to one stage. Replaces the former free-text stage name.</param>
    /// <param name="pipelineId">
    /// Which pipeline's deals to list. Omitted means the default pipeline — NOT every
    /// pipeline. If absence widened the scope, a tenant that created a second pipeline would
    /// silently start seeing both funnels merged in a list that still says it is showing one.
    /// Pass "all" to ask for every pipeline deliberately; the response says which it did.
    /// </param>
    internal static async Task<Results<Ok<OpportunityList>, BadRequest<string>>> Handle(
        string? search,
        Guid? stageId,
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
            db, PipelineRecordType.Opportunity, pipelineId, cancellationToken);

        if (!scope.IsValid) return TypedResults.BadRequest(scope.Error!);

        var scopedPipelines = scope.PipelineIds;

        // AsNoTracking: this handler reads and never saves. The Includes stay — unlike the
        // lead list, several stage fields (StageType, IsTerminal, Order) are read per row,
        // so a projection would buy little and risk more.
        var query = db.Opportunities
            .AsNoTracking()
            .Include(o => o.Contact)
            .Include(o => o.Stage)
            // The pipeline filter is applied BEFORE anything else and is never optional:
            // every row returned is from a pipeline the caller asked for.
            .Where(o => scopedPipelines.Contains(o.PipelineId))
            .Include(o => o.Pipeline)
            .AsQueryable();

        if (stageId is { } filterStageId)
            query = query.Where(o => o.PipelineStageId == filterStageId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(o =>
                EF.Functions.ILike(o.Title, pattern) ||
                EF.Functions.ILike(o.Contact.Name, pattern));
        }

        var opportunities = await query
            .OrderByDescending(o => o.CreatedAt)
            .Take(500)
            .ToListAsync(cancellationToken);

        var result = opportunities.Select(o => new OpportunityItem(
            o.Id, o.Title, o.ContactId, o.Contact?.Name ?? "—",
            o.PipelineStageId,
            o.Stage?.Name ?? "—",
            (o.Stage?.StageType ?? StageType.Active).ToString(),
            o.Stage?.IsTerminal ?? false,
            o.Stage?.Order ?? int.MaxValue,
            o.PipelineId,
            o.Pipeline?.Name ?? "—",
            o.Amount, o.ExpectedCloseDate, o.LossReason, o.CreatedAt)).ToList();

        return TypedResults.Ok(new OpportunityList(
            result,
            scope.SelectedPipelineId,
            scope.ScopeLabel,
            scope.IsTenantWide,
            scope.IsMultiPipelineTenant));
    }
}
