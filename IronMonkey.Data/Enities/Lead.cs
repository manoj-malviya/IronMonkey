using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public sealed class Lead : BaseTenantEntity
{
    private Lead(Guid id, Guid tenantId, string firstName, string lastName, string mobile, string email, string leadSource)
        : base(id, tenantId)
    {
        FirstName = firstName;
        LastName = lastName;
        Mobile = mobile;
        Email = email;
        LeadSource = leadSource;
        IsConverted = false;
    }

    private Lead()
    {
    }

    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Mobile { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string LeadSource { get; private set; } = string.Empty;
    public Guid? ConvertedAccountId { get; private set; }
    public Guid? ConvertedContactId { get; private set; }
    public Guid? ConvertedOpportunityId { get; private set; }
    public bool IsConverted { get; private set; }

    public static Lead Create(Guid tenantId, string firstName, string lastName, string mobile, string email, string leadSource)
    {
        return new Lead(Guid.NewGuid(), tenantId, firstName, lastName, mobile, email, leadSource);
    }

    public void UpdateLeadInfo(string firstName, string lastName, string mobile, string email, string leadSource)
    {
        FirstName = firstName;
        LastName = lastName;
        Mobile = mobile;
        Email = email;
        LeadSource = leadSource;
    }

    public void Convert(Guid? accountId, Guid? contactId, Guid? opportunityId)
    {
        ConvertedAccountId = accountId;
        ConvertedContactId = contactId;
        ConvertedOpportunityId = opportunityId;
        IsConverted = true;
    }
}
