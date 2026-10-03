using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

/// <summary>
/// One recorded stage movement of one record — the history that stage-duration and velocity
/// reporting is computed from.
///
/// This is deliberately NOT <see cref="StageTransition"/>. That entity looks like history but
/// is not: it is the tenant's *allowed-edges graph*, one row per permitted (from, to) pair,
/// uniquely indexed on exactly that pair and read by <c>StateValidationService</c> to decide
/// whether a move is legal. Adding a record reference and a timestamp to it would break its
/// uniqueness constraint on the second identical move and turn the state machine's
/// configuration into an append-only log, silently disabling the validation that reads it.
///
/// So the two concerns are separated: the graph says what MAY happen, this says what DID.
/// Both leads and opportunities record here — one coherent model, discriminated by
/// <see cref="PipelineRecordType"/> — so duration and velocity are computed the same way for
/// each rather than by two divergent queries.
/// </summary>
public sealed class StageChange : BaseTenantEntity
{
    private StageChange() { }

    /// <summary>Which kind of record moved, matching the stages' own record type.</summary>
    public PipelineRecordType RecordType { get; private set; }

    /// <summary>
    /// The lead or opportunity that moved. This is the field whose absence made duration
    /// reporting impossible: without it a row says a move happened somewhere in the tenant
    /// but not to what, so no record's time-in-stage can be reconstructed.
    ///
    /// Intentionally not a foreign key: history must survive the record being hard-deleted,
    /// and the column serves two different tables depending on <see cref="RecordType"/>.
    /// </summary>
    public Guid RecordId { get; private set; }

    /// <summary>Null for the first placement — a record entering the pipeline came from nowhere.</summary>
    public Guid? FromStageId { get; private set; }

    public Guid ToStageId { get; private set; }

    /// <summary>
    /// The pipeline the record was in before the move, or null for a first placement.
    /// </summary>
    public Guid? FromPipelineId { get; private set; }

    /// <summary>
    /// The pipeline the record is in after the move.
    ///
    /// Recorded alongside the stages so a cross-pipeline move is legible as one in the
    /// record's own history rather than reading as an inexplicable jump between two stages
    /// that were never adjacent. It is also what lets a per-pipeline velocity report exclude
    /// the segment a record spent in a different pipeline, instead of attributing that time
    /// to a pipeline the record was not in.
    /// </summary>
    public Guid ToPipelineId { get; private set; }

    /// <summary>True when this row moved the record between pipelines, not just between
    /// stages. Derived, not stored — the two ids above are the facts.</summary>
    public bool IsPipelineChange => FromPipelineId.HasValue && FromPipelineId.Value != ToPipelineId;

    /// <summary>
    /// When the move happened, recorded explicitly rather than inferred from
    /// <c>CreatedAt</c>. The base timestamp is stamped by the DbContext at save time, which
    /// is close but not the same thing, and is silently rewritten by any later save —
    /// duration arithmetic over a column that can move is not arithmetic.
    /// </summary>
    public DateTime OccurredAt { get; private set; }

    /// <summary>
    /// The user who moved it, or null for a system action — an import, a workflow rule, or
    /// a migration backfill. Null rather than <see cref="Guid.Empty"/> so "nobody" is
    /// distinguishable from a user id that failed to resolve.
    /// </summary>
    public Guid? ChangedByUserId { get; private set; }

    public PipelineStage? FromStage { get; private set; }
    public PipelineStage ToStage { get; private set; } = null!;

    public static StageChange Record(
        Guid tenantId,
        PipelineRecordType recordType,
        Guid recordId,
        Guid? fromStageId,
        Guid toStageId,
        Guid? changedByUserId,
        DateTime? occurredAt = null,
        Guid? fromPipelineId = null,
        Guid toPipelineId = default)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RecordType = recordType,
            RecordId = recordId,
            FromStageId = fromStageId,
            ToStageId = toStageId,
            FromPipelineId = fromPipelineId,
            ToPipelineId = toPipelineId,
            ChangedByUserId = changedByUserId,
            OccurredAt = occurredAt ?? DateTime.UtcNow
        };
}
