using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public enum LeadSource { Manual = 0, Import = 1, Api = 2, WebForm = 3 }

public sealed class Lead : BaseTenantEntity
{
    private Lead(Guid id, Guid tenantId, string firstName, string lastName, string mobile, string email, LeadSource source, Guid pipelineStageId)
        : base(id, tenantId)
    {
        FirstName = firstName;
        LastName = lastName;
        Mobile = mobile;
        Email = email;
        Source = source;
        PipelineStageId = pipelineStageId;
        IsConverted = false;
    }

    private Lead()
    {
    }

    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Mobile { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public LeadSource Source { get; private set; } = LeadSource.Manual;
    public Guid PipelineStageId { get; private set; }

    /// <summary>
    /// The pipeline this lead runs in. Non-nullable: a record outside every pipeline appears
    /// on no board and in no pipeline-scoped total, so the state is forbidden rather than
    /// handled. Kept denormalised beside <see cref="PipelineStageId"/> — rather than read
    /// through <c>Stage.PipelineId</c> — so a list or dashboard filtered by pipeline is an
    /// index seek on the lead table, not a join for every row.
    ///
    /// The two are held consistent by <see cref="MoveToPipelineStage"/>, which is the only
    /// way either changes.
    /// </summary>
    public Guid PipelineId { get; private set; }

    public CustomFieldValues CustomFields { get; private set; } = new();

    /// <summary>
    /// The mobile number reduced to digits, maintained by PostgreSQL as a stored generated
    /// column and trigram-indexed, so search can match "07700 900123" against "+447700900123"
    /// without normalising every row at query time. Read-only to the application.
    /// </summary>
    public string MobileDigits { get; private set; } = string.Empty;
    public PipelineStage Stage { get; private set; } = null!;
    public Pipeline Pipeline { get; private set; } = null!;
    public Guid? ConvertedAccountId { get; private set; }
    public Guid? ConvertedContactId { get; private set; }
    public Guid? ConvertedOpportunityId { get; private set; }
    public bool IsConverted { get; private set; }
    public bool IsPotentialDuplicate { get; private set; }
    public Guid? PotentialDuplicateLeadId { get; private set; }
    public Guid? AssignedToUserId { get; private set; }

    public static Lead Create(Guid tenantId, string firstName, string lastName, string mobile, string email, LeadSource source, Guid pipelineStageId)
    {
        return new Lead(Guid.NewGuid(), tenantId, firstName, lastName, mobile, email, source, pipelineStageId);
    }

    public void UpdateLeadInfo(string firstName, string lastName, string mobile, string email, LeadSource source)
    {
        FirstName = firstName;
        LastName = lastName;
        Mobile = mobile;
        Email = email;
        Source = source;
    }

    public void Convert(Guid? accountId, Guid? contactId, Guid? opportunityId)
    {
        ConvertedAccountId = accountId;
        ConvertedContactId = contactId;
        ConvertedOpportunityId = opportunityId;
        IsConverted = true;
    }

    public void MarkAsPotentialDuplicate(Guid matchedLeadId)
    {
        IsPotentialDuplicate = true;
        PotentialDuplicateLeadId = matchedLeadId;
    }

    public void AssignTo(Guid? userId) => AssignedToUserId = userId;

    /// <summary>
    /// Moves the lead to a stage within its current pipeline.
    ///
    /// This overload deliberately does not touch <see cref="PipelineId"/>: an ordinary stage
    /// move must never change which pipeline a record is in, and the callers that use it
    /// have already validated the stage against the lead's own pipeline.
    /// </summary>
    public void MoveToPipelineStage(Guid stageId) => PipelineStageId = stageId;

    /// <summary>
    /// Moves the lead into a different pipeline, landing on an explicitly chosen stage of
    /// that pipeline.
    ///
    /// Both values are set together and by one method because setting either alone is the
    /// central correctness risk of multiple pipelines: a pipeline change that kept the old
    /// stage would leave the record pointing at a stage from a pipeline it is no longer in —
    /// invisible on its new board, and counted in the old pipeline's per-stage totals. The
    /// caller resolves <paramref name="stageId"/> within <paramref name="pipelineId"/> and
    /// the move is refused outright if it cannot.
    /// </summary>
    public void MoveToPipeline(Guid pipelineId, Guid stageId)
    {
        PipelineId = pipelineId;
        PipelineStageId = stageId;
    }

    /// <summary>Places the lead in a pipeline at creation or during seeding.</summary>
    public void SetPipeline(Guid pipelineId) => PipelineId = pipelineId;
}
