using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public sealed class Opportunity : BaseTenantEntity
{
    private Opportunity(Guid id, Guid tenantId, string title, Guid contactId, DateTime expectedCloseDate, Guid pipelineStageId)
        : base(id, tenantId)
    {
        Title = title;
        ContactId = contactId;
        ExpectedCloseDate = expectedCloseDate;
        PipelineStageId = pipelineStageId;
    }

    private Opportunity()
    {
    }

    public string Title { get; private set; } = string.Empty;
    public Guid ContactId { get; private set; }
    public DateTime ExpectedCloseDate { get; private set; }

    /// <summary>
    /// The tenant-configured stage this opportunity sits in. Replaces the former free-text
    /// <c>Stage</c> column: a string could hold anything, could not be renamed without
    /// rewriting every row, and forced won/lost to be decided by matching a magic name.
    /// </summary>
    public Guid PipelineStageId { get; private set; }

    /// <summary>
    /// The pipeline this deal runs in. Non-nullable and denormalised beside the stage for
    /// the same reasons as <see cref="Lead.PipelineId"/>: a deal outside every pipeline is
    /// not a state worth supporting, and a pipeline-scoped dashboard aggregate must not have
    /// to join the stage table for every row it sums.
    /// </summary>
    public Guid PipelineId { get; private set; }

    public string? LossReason { get; private set; }
    public decimal Amount { get; private set; } = 0m;

    // Navigation properties
    public Contact Contact { get; private set; } = null!;
    public PipelineStage Stage { get; private set; } = null!;
    public Pipeline Pipeline { get; private set; } = null!;

    public static Opportunity Create(Guid tenantId, string title, Guid contactId, DateTime expectedCloseDate, Guid pipelineStageId)
    {
        return new Opportunity(Guid.NewGuid(), tenantId, title, contactId, expectedCloseDate, pipelineStageId);
    }

    public void UpdateOpportunity(string title, Guid contactId, DateTime expectedCloseDate, Guid pipelineStageId)
    {
        Title = title;
        ContactId = contactId;
        ExpectedCloseDate = expectedCloseDate;
        PipelineStageId = pipelineStageId;
    }

    /// <summary>
    /// Closes the deal as lost against a stage the caller has already resolved by
    /// <see cref="StageType.ClosedLost"/>. The stage id is a parameter rather than a name
    /// looked up here because the entity has no database access — and because the whole
    /// point of the change is that "Lost" is no longer a name this code may assume exists.
    /// A tenant that renamed it to "Declined" closes deals through the same path.
    /// </summary>
    public void MarkAsLost(Guid lostStageId, string lossReason)
    {
        LossReason = lossReason;
        PipelineStageId = lostStageId;
    }

    /// <summary>Moves the deal to a stage within its current pipeline. Does not change
    /// <see cref="PipelineId"/> — see <see cref="MoveToPipeline"/>.</summary>
    public void MoveToPipelineStage(Guid stageId) => PipelineStageId = stageId;

    /// <summary>
    /// Moves the deal into a different pipeline, landing on an explicitly chosen stage of
    /// that pipeline. Set together for the reason given on <see cref="Lead.MoveToPipeline"/>:
    /// a deal left on its old stage after a pipeline change is counted in the wrong
    /// pipeline's value totals and shows on neither board.
    /// </summary>
    public void MoveToPipeline(Guid pipelineId, Guid stageId)
    {
        PipelineId = pipelineId;
        PipelineStageId = stageId;
    }

    /// <summary>Places the deal in a pipeline at creation or during seeding.</summary>
    public void SetPipeline(Guid pipelineId) => PipelineId = pipelineId;

    /// <summary>
    /// Clears a loss reason carried over from an earlier close. Reopening a deal that keeps
    /// the old reason attached reads, in every report, as though it were still lost.
    /// </summary>
    public void ClearLossReason() => LossReason = null;

    public void SetAmount(decimal amount) => Amount = amount;
}
