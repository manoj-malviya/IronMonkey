using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Pipelines;

/// <summary>
/// Lists the tenant's pipelines for one record type.
///
/// <see cref="Response.IsMultiPipeline"/> is what every UI surface reads to decide whether a
/// pipeline picker appears at all. A tenant with one pipeline gets <c>false</c> and renders
/// nothing — that is the mechanism by which a single-pipeline tenant is never asked to
/// choose a pipeline anywhere.
/// </summary>
public class ListPipelinesEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/pipelines", Handle)
        .WithSummary("List the tenant's pipelines for a record type")
        .WithTags("Pipelines")
        .RequireAuthorization();

    public record PipelineItem(
        Guid Id, string Name, string RecordType, bool IsDefault, bool IsActive,
        int Order, string? Description, int StageCount, int RecordCount);

    /// <param name="IsMultiPipeline">
    /// True when this tenant has more than one pipeline of this record type. The single
    /// signal the UI needs to decide whether to show a picker.
    /// </param>
    public record Response(List<PipelineItem> Items, bool IsMultiPipeline, Guid? DefaultPipelineId);

    /// <param name="recordType">"Lead" or "Opportunity". Defaults to Lead.</param>
    /// <param name="includeInactive">
    /// Nullable on purpose — a non-nullable bool query parameter is *required* in minimal
    /// APIs, so every caller that omitted it would 500 rather than defaulting.
    /// </param>
    /// <param name="includeCounts">
    /// Opt *out*, not opt in. The stage and record counts each cost a GROUP BY, the record
    /// one over every non-deleted row of the type, and a caller that renders those numbers
    /// and forgot the flag would print a confident 0 rather than the truth. So the default
    /// stays correct and a caller that does not read them says so.
    /// Nullable for the same reason as <paramref name="includeInactive"/>.
    /// </param>
    internal static async Task<Results<Ok<Response>, BadRequest<string>>> Handle(
        string? recordType,
        bool? includeInactive,
        bool? includeCounts,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!PipelineRecordTypes.TryParse(recordType, out var type))
            return TypedResults.BadRequest(PipelineRecordTypes.ParseError);

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var query = db.Pipelines.Where(p => p.RecordType == type);

        // Counted BEFORE the active filter: whether the tenant is multi-pipeline is a fact
        // about the tenant, not about the current filter. Deriving it from a filtered list
        // would make a picker disappear the moment the second pipeline was deactivated,
        // stranding every record still in it.
        var totalCount = await db.Pipelines.CountAsync(p => p.RecordType == type, cancellationToken);

        if (includeInactive != true)
            query = query.Where(p => p.IsActive);

        var pipelines = await query
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Order)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

        var ids = pipelines.Select(p => p.Id).ToList();

        var wantCounts = includeCounts != false;

        var stageCounts = wantCounts
            ? await db.PipelineStages
                .Where(s => ids.Contains(s.PipelineId))
                .GroupBy(s => s.PipelineId)
                .Select(g => new { PipelineId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.PipelineId, x => x.Count, cancellationToken)
            : [];

        // Records are counted from whichever table this record type lives in. Both are
        // grouped by PipelineId so each pipeline reports only its own — this is the count a
        // picker shows beside a name, and one pipeline's figure appearing beside another's
        // name is exactly the silent-wrong-number failure being guarded against.
        var recordCounts = !wantCounts
            ? []
            : type == PipelineRecordType.Lead
            ? await db.Leads
                .Where(l => ids.Contains(l.PipelineId))
                .GroupBy(l => l.PipelineId)
                .Select(g => new { PipelineId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.PipelineId, x => x.Count, cancellationToken)
            : await db.Opportunities
                .Where(o => ids.Contains(o.PipelineId))
                .GroupBy(o => o.PipelineId)
                .Select(g => new { PipelineId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.PipelineId, x => x.Count, cancellationToken);

        var items = pipelines.Select(p => new PipelineItem(
            p.Id, p.Name, p.RecordType.ToString(), p.IsDefault, p.IsActive, p.Order, p.Description,
            stageCounts.GetValueOrDefault(p.Id),
            recordCounts.GetValueOrDefault(p.Id))).ToList();

        return TypedResults.Ok(new Response(
            items,
            IsMultiPipeline: totalCount > 1,
            DefaultPipelineId: pipelines.FirstOrDefault(p => p.IsDefault)?.Id));
    }
}

/// <summary>
/// Parses the <c>recordType</c> query parameter the pipeline endpoints share.
///
/// Absence means Lead, matching <see cref="PipelineRecordType"/>'s own default and every
/// caller written before opportunities had pipelines. An unrecognised value is refused
/// rather than silently falling back — defaulting a typo to Lead would show a caller asking
/// for deal pipelines a list of lead ones.
/// </summary>
internal static class PipelineRecordTypes
{
    public const string ParseError = "recordType must be 'Lead' or 'Opportunity'.";

    public static bool TryParse(string? value, out PipelineRecordType recordType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            recordType = PipelineRecordType.Lead;
            return true;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out recordType)
            && Enum.IsDefined(recordType);
    }
}
