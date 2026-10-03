using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Configuration;

/// <summary>
/// Writes stage history for leads and opportunities alike.
///
/// One helper rather than a call to <c>StageChange.Record</c> at each site, so that the two
/// record types cannot drift into recording different things — the reporting that reads this
/// table computes duration identically for both, and it can only do that if both are written
/// identically.
///
/// A no-op move is not recorded: saving a form without touching the stage is not a
/// transition, and a history full of them makes time-in-stage read as zero. A pipeline change
/// is always recorded even if the stage id somehow matched, because moving between pipelines
/// is an event in its own right.
/// </summary>
public static class StageChangeRecorder
{
    /// <summary>
    /// Records a move if the stage or the pipeline actually changed, and returns whether it
    /// did. The caller still has to save.
    /// </summary>
    /// <param name="fromPipelineId">
    /// The pipeline the record was in, or null for a first placement.
    /// </param>
    /// <param name="toPipelineId">
    /// The pipeline the record is in after the move. Required: a history row that does not
    /// say which pipeline it happened in cannot be excluded from another pipeline's velocity
    /// report, so every pipeline's numbers would include every other pipeline's moves.
    /// </param>
    public static bool Record(
        TenantDbContext db,
        Guid tenantId,
        PipelineRecordType recordType,
        Guid recordId,
        Guid? fromStageId,
        Guid toStageId,
        Guid? changedByUserId,
        Guid? fromPipelineId,
        Guid toPipelineId)
    {
        var pipelineChanged = fromPipelineId.HasValue && fromPipelineId.Value != toPipelineId;

        if (fromStageId == toStageId && !pipelineChanged) return false;

        db.StageChanges.Add(StageChange.Record(
            tenantId, recordType, recordId, fromStageId, toStageId, changedByUserId,
            occurredAt: null, fromPipelineId: fromPipelineId, toPipelineId: toPipelineId));

        return true;
    }
}
