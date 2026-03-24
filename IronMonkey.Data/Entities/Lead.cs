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
    public CustomFieldValues CustomFields { get; private set; } = new();
    public PipelineStage Stage { get; private set; } = null!;
    public Guid? ConvertedAccountId { get; private set; }
    public Guid? ConvertedContactId { get; private set; }
    public Guid? ConvertedOpportunityId { get; private set; }
    public bool IsConverted { get; private set; }
    public bool IsPotentialDuplicate { get; private set; }
    public Guid? PotentialDuplicateLeadId { get; private set; }

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
}
