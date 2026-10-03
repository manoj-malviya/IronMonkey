using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Pipelines;

/// <summary>
/// Resolves stages <b>within a named pipeline</b>.
///
/// Every helper here takes a pipeline id and filters on it. That is not incidental: the
/// central correctness risk of multiple pipelines is a record landing on a stage belonging
/// to a different pipeline — it then shows on no board, is excluded from its own pipeline's
/// per-stage totals and is counted in another's. A stage lookup that does not name a
/// pipeline cannot rule that out, so none of these offer one.
/// </summary>
public static class PipelineStageResolution
{
    /// <summary>All stages of one pipeline.</summary>
    public static IQueryable<PipelineStage> Stages(TenantDbContext db, Guid pipelineId) =>
        db.PipelineStages.Where(s => s.PipelineId == pipelineId);

    /// <summary>
    /// The stage with this id <b>if it belongs to this pipeline</b>, else null. The caller
    /// treats null as "refuse the move", never as "use a default" — silently substituting a
    /// stage for one the caller named is how a record ends up somewhere nobody chose.
    /// </summary>
    public static Task<PipelineStage?> FindInPipelineAsync(
        TenantDbContext db, Guid pipelineId, Guid stageId, CancellationToken ct) =>
        db.PipelineStages.SingleOrDefaultAsync(s => s.Id == stageId && s.PipelineId == pipelineId, ct);

    /// <summary>
    /// The stage a record entering this pipeline starts in: the first active non-terminal
    /// stage in order, preferring an explicit <see cref="StageType.Entry"/>. Null when the
    /// pipeline has no usable stage at all, which callers report rather than working around.
    /// </summary>
    public static async Task<PipelineStage?> GetEntryStageAsync(
        TenantDbContext db, Guid pipelineId, CancellationToken ct)
    {
        var open = await Stages(db, pipelineId)
            .Where(s => s.IsActive && s.StageType != StageType.ClosedWon && s.StageType != StageType.ClosedLost)
            .OrderBy(s => s.Order)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

        return open.FirstOrDefault(s => s.StageType == StageType.Entry) ?? open.FirstOrDefault();
    }

    /// <summary>
    /// The stage a record closes into for a terminal type, within one pipeline — the
    /// lowest-ordered active one, so a pipeline with several "lost" stages gives a stable
    /// answer. Resolved by <see cref="StageType"/> and never by name, so a pipeline that
    /// renamed "Lost" to "Declined" still closes.
    /// </summary>
    public static Task<PipelineStage?> GetTerminalStageAsync(
        TenantDbContext db, Guid pipelineId, StageType terminalType, CancellationToken ct) =>
        Stages(db, pipelineId)
            .Where(s => s.StageType == terminalType && s.IsActive)
            .OrderBy(s => s.Order)
            .ThenBy(s => s.Id)
            .FirstOrDefaultAsync(ct);

}
