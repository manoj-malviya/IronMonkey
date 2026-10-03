using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Opportunities;

/// <summary>
/// Resolves opportunity stages for the endpoints that create, update and close deals.
///
/// Every lookup here goes through <see cref="PipelineRecordType.Opportunity"/>. Without that
/// predicate a caller could pass a lead stage id and the opportunity would land in the lead
/// pipeline — the two record types share one table, so the filter is the only thing keeping
/// them apart.
///
/// Terminal resolution is by <see cref="StageType"/> and never by name: a tenant that renamed
/// "Lost" to "Declined" must still be able to close a deal, and reporting must still count it
/// as lost.
/// </summary>
public static class OpportunityStageResolver
{
    public static IQueryable<PipelineStage> Stages(TenantDbContext db) =>
        db.PipelineStages.Where(s => s.RecordType == PipelineRecordType.Opportunity);

    /// <summary>The stage with this id, or null if it is not an opportunity stage of this tenant.</summary>
    public static Task<PipelineStage?> FindAsync(TenantDbContext db, Guid stageId, CancellationToken ct) =>
        Stages(db).SingleOrDefaultAsync(s => s.Id == stageId, ct);

    /// <summary>
    /// The stage a new opportunity starts in when the caller named none: the first active
    /// non-terminal stage in order, preferring an explicit <see cref="StageType.Entry"/>.
    /// Creating a deal directly into a closed stage is not a default anyone wants.
    /// </summary>
    public static async Task<PipelineStage?> GetDefaultEntryAsync(TenantDbContext db, CancellationToken ct)
    {
        var open = await Stages(db)
            .Where(s => s.IsActive && s.StageType != StageType.ClosedWon && s.StageType != StageType.ClosedLost)
            .OrderBy(s => s.Order)
            .ToListAsync(ct);

        return open.FirstOrDefault(s => s.StageType == StageType.Entry) ?? open.FirstOrDefault();
    }

    /// <summary>
    /// The stage a deal closes into for the given terminal type — the lowest-ordered active
    /// one, so a tenant with several "lost" stages gets a stable, predictable answer.
    /// </summary>
    public static Task<PipelineStage?> GetTerminalAsync(
        TenantDbContext db, StageType terminalType, CancellationToken ct) =>
        Stages(db)
            .Where(s => s.StageType == terminalType && s.IsActive)
            .OrderBy(s => s.Order)
            .FirstOrDefaultAsync(ct);
}
