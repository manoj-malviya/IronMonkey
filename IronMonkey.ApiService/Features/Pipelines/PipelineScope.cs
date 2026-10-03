using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Pipelines;

/// <summary>
/// How a request selected pipelines, and the ids that selection resolves to.
///
/// Three states, deliberately distinct, because collapsing any two of them is how a
/// multi-pipeline tenant ends up reading one pipeline's number as the tenant total:
///
/// <list type="bullet">
/// <item><b>Single</b> — one pipeline, named or defaulted. Aggregates are that pipeline's.</item>
/// <item><b>All</b> — every pipeline, asked for explicitly with <c>?pipelineId=all</c>.
///   Aggregates are the tenant total and say so.</item>
/// <item><b>Invalid</b> — a pipeline id that is not this tenant's, or not of the right
///   record type. Refused, never quietly treated as "all": a typo'd id that widened the
///   scope would present the tenant total under a pipeline's name.</item>
/// </list>
/// </summary>
/// <param name="PipelineIds">
/// Every pipeline in scope. One entry for <see cref="PipelineScopeKind.Single"/>, all of the
/// tenant's for <see cref="PipelineScopeKind.All"/>. Callers filter on membership of this
/// list rather than branching on the kind, so the two paths cannot diverge.
/// </param>
/// <param name="IsTenantWide">
/// True only for <see cref="PipelineScopeKind.All"/>. Every response that carries a total
/// echoes this, so a client can label the figure "all pipelines" instead of leaving the
/// reader to assume which one it is.
/// </param>
/// <param name="IsMultiPipelineTenant">
/// Whether the tenant has more than one pipeline of this record type at all. This is what
/// the UI reads to decide whether to render a pipeline picker: a tenant with one pipeline is
/// never shown one, and therefore never asked to make a choice that has only one answer.
/// </param>
public sealed record PipelineScope(
    PipelineScopeKind Kind,
    IReadOnlyList<Guid> PipelineIds,
    Guid? SelectedPipelineId,
    string? SelectedPipelineName,
    bool IsTenantWide,
    bool IsMultiPipelineTenant,
    string? Error)
{
    public bool IsValid => Kind != PipelineScopeKind.Invalid;

    /// <summary>True when the given record's pipeline is in scope.</summary>
    public bool Contains(Guid pipelineId) => PipelineIds.Contains(pipelineId);

    /// <summary>
    /// A label for the figures produced under this scope, so a response never presents a
    /// number without saying what it covers.
    /// </summary>
    public string ScopeLabel => Kind switch
    {
        PipelineScopeKind.All => "All pipelines",
        // No name for a tenant that has no pipelines yet. Empty rather than a placeholder, so
        // a UI renders nothing instead of a label for a pipeline that does not exist.
        PipelineScopeKind.Single => SelectedPipelineName ?? string.Empty,
        _ => "Invalid"
    };

}

public enum PipelineScopeKind { Single = 0, All = 1, Invalid = 2 }

/// <summary>
/// Resolves the <c>pipelineId</c> query parameter every pipeline-aware endpoint accepts.
///
/// <para>
/// <b>The parameter is a string, not a Guid.</b> It has to carry a third value — the literal
/// <c>all</c> — alongside a pipeline id and absence, and a <c>Guid?</c> has no room for it.
/// Absence and "all" are genuinely different requests: absence means "you choose", which for
/// a single-pipeline tenant is the only pipeline and for a multi-pipeline tenant is the
/// default one, while "all" is an explicit ask for the tenant-wide total.
/// </para>
///
/// <para>
/// <b>Absence never means "all".</b> That is the single most important rule here. If an
/// omitted parameter widened the scope, then every pre-existing client — the lead list, the
/// board, all four dashboard widgets — would silently start summing across pipelines the day
/// a tenant created a second one, and would present that sum under the first pipeline's
/// heading. Absence resolves to exactly one pipeline, always.
/// </para>
/// </summary>
public interface IPipelineScopeResolver
{
    Task<PipelineScope> ResolveAsync(
        TenantDbContext db,
        PipelineRecordType recordType,
        string? pipelineId,
        CancellationToken ct);

    /// <summary>
    /// The tenant's default pipeline for a record type, creating nothing. Null only for a
    /// tenant whose provisioning predates pipelines and was never migrated — every path
    /// that can hit that treats it as a configuration error rather than inventing one.
    /// </summary>
    Task<Pipeline?> GetDefaultAsync(TenantDbContext db, PipelineRecordType recordType, CancellationToken ct);
}

public sealed class PipelineScopeResolver : IPipelineScopeResolver
{
    /// <summary>The token a caller sends to ask for every pipeline at once.</summary>
    public const string AllToken = "all";

    public async Task<PipelineScope> ResolveAsync(
        TenantDbContext db,
        PipelineRecordType recordType,
        string? pipelineId,
        CancellationToken ct)
    {
        // Counted, not materialised, and bounded at two: the only question is whether the
        // tenant has more than one pipeline. Loading every row to compute a boolean put a
        // full table read on the hot path of every pipeline-aware endpoint.
        //
        // Counted over every pipeline including inactive ones: an inactive pipeline still
        // holds records that a tenant-wide total must include, and a tenant that retired its
        // second pipeline has still seen a picker and should keep seeing one.
        var pipelineCount = await db.Pipelines
            .Where(p => p.RecordType == recordType)
            .Take(2)
            .CountAsync(ct);

        var isMulti = pipelineCount > 1;

        // A tenant with no pipelines of this record type yet — brand new, or one whose
        // configuration has not been seeded. This is NOT an error.
        //
        // It resolves to a valid, empty scope: the list endpoints then return no rows and the
        // dashboards return zeros, which is the honest answer and what those surfaces already
        // did before pipelines existed. Refusing instead would turn an empty tenant's
        // dashboard into four error panels, and "you have no leads" is a different message
        // from "something is broken".
        //
        // The empty PipelineIds list also makes every `Contains(record.PipelineId)` predicate
        // match nothing, so the scoping stays correct rather than silently falling open.
        if (pipelineCount == 0)
        {
            return new PipelineScope(
                PipelineScopeKind.Single, [], null, null,
                IsTenantWide: false, IsMultiPipelineTenant: false, Error: null);
        }

        var trimmed = pipelineId?.Trim();

        if (string.Equals(trimmed, AllToken, StringComparison.OrdinalIgnoreCase))
        {
            // The one branch that genuinely needs every row. Projected to ids only.
            var allIds = await db.Pipelines
                .Where(p => p.RecordType == recordType)
                .OrderByDescending(p => p.IsDefault)
                .ThenBy(p => p.Order)
                .ThenBy(p => p.Name)
                .Select(p => p.Id)
                .ToListAsync(ct);

            return new PipelineScope(
                PipelineScopeKind.All,
                allIds,
                SelectedPipelineId: null,
                SelectedPipelineName: null,
                IsTenantWide: true,
                IsMultiPipelineTenant: isMulti,
                Error: null);
        }

        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            if (!Guid.TryParse(trimmed, out var requested))
            {
                return new PipelineScope(
                    PipelineScopeKind.Invalid, [], null, null, false, isMulti,
                    $"'{trimmed}' is not a pipeline id. Pass a pipeline id or '{AllToken}'.");
            }

            // Matched against THIS tenant's pipelines OF THIS RECORD TYPE. A lead pipeline id
            // passed to an opportunity endpoint does not match, so it is refused rather than
            // producing an empty board that reads as "no deals".
            //
            // Deliberately NOT filtered on IsActive: a request naming a retired pipeline
            // resolved before and must keep resolving, or a tenant loses sight of the records
            // still sitting in it.
            var match = await db.Pipelines
                .Where(p => p.RecordType == recordType && p.Id == requested)
                .Select(p => new { p.Id, p.Name })
                .FirstOrDefaultAsync(ct);

            if (match is null)
            {
                return new PipelineScope(
                    PipelineScopeKind.Invalid, [], null, null, false, isMulti,
                    "The requested pipeline does not exist for this tenant and record type.");
            }

            return new PipelineScope(
                PipelineScopeKind.Single, [match.Id], match.Id, match.Name,
                IsTenantWide: false, IsMultiPipelineTenant: isMulti, Error: null);
        }

        // Absent. Resolve to exactly one pipeline — the default, or the only one there is.
        // Same ordering as the materialised list used, so the same row wins.
        var fallback = await db.Pipelines
            .Where(p => p.RecordType == recordType)
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Order)
            .ThenBy(p => p.Name)
            .Select(p => new { p.Id, p.Name })
            .FirstAsync(ct);

        return new PipelineScope(
            PipelineScopeKind.Single, [fallback.Id], fallback.Id, fallback.Name,
            IsTenantWide: false, IsMultiPipelineTenant: isMulti, Error: null);
    }

    public Task<Pipeline?> GetDefaultAsync(
        TenantDbContext db, PipelineRecordType recordType, CancellationToken ct) =>
        db.Pipelines
            .Where(p => p.RecordType == recordType)
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Order)
            .ThenBy(p => p.Name)
            .FirstOrDefaultAsync(ct);
}
