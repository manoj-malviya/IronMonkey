using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Pipelines;

/// <summary>
/// Resolves the optional <c>PipelineId</c> a write request may carry, into exactly one
/// pipeline.
///
/// <para>
/// This is the write-side companion to <see cref="IPipelineScopeResolver"/>, which handles
/// reads. A write always targets one pipeline — there is no "all" for creating a stage — so
/// the shape is simpler: a named pipeline, or the tenant's default.
/// </para>
///
/// <para>
/// The default fallback is what keeps a single-pipeline tenant's forms unchanged. A create
/// form that never sends a pipeline id keeps working exactly as it did, because the only
/// pipeline the tenant has is also its default.
/// </para>
/// </summary>
public static class PipelineTarget
{
    /// <summary>
    /// The named pipeline if it exists for this tenant AND this record type, else the
    /// tenant's default for that record type. Null means the caller named a pipeline that
    /// does not qualify — a 400, never a silent fallback to the default, because writing a
    /// stage into a pipeline the caller did not ask for is how the two funnels get mixed.
    /// </summary>
    public static async Task<Pipeline?> ResolveAsync(
        TenantDbContext db,
        PipelineRecordType recordType,
        Guid? requestedPipelineId,
        CancellationToken ct)
    {
        if (requestedPipelineId is { } requested)
        {
            return await db.Pipelines.SingleOrDefaultAsync(
                p => p.Id == requested && p.RecordType == recordType, ct);
        }

        return await db.Pipelines
            .Where(p => p.RecordType == recordType)
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Order)
            .ThenBy(p => p.Name)
            .FirstOrDefaultAsync(ct);
    }
}
