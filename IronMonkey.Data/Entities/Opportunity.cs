using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public sealed class Opportunity : BaseTenantEntity
{
    private Opportunity(Guid id, Guid tenantId, string title, Guid contactId, DateTime expectedCloseDate, string stage)
        : base(id, tenantId)
    {
        Title = title;
        ContactId = contactId;
        ExpectedCloseDate = expectedCloseDate;
        Stage = stage;
    }

    private Opportunity()
    {
    }

    public string Title { get; private set; } = string.Empty;
    public Guid ContactId { get; private set; }
    public DateTime ExpectedCloseDate { get; private set; }
    public string Stage { get; private set; } = string.Empty;
    public string? LossReason { get; private set; }
    public decimal Amount { get; private set; } = 0m;

    // Navigation property
    public Contact Contact { get; private set; } = null!;

    public static Opportunity Create(Guid tenantId, string title, Guid contactId, DateTime expectedCloseDate, string stage)
    {
        return new Opportunity(Guid.NewGuid(), tenantId, title, contactId, expectedCloseDate, stage);
    }

    public void UpdateOpportunity(string title, Guid contactId, DateTime expectedCloseDate, string stage)
    {
        Title = title;
        ContactId = contactId;
        ExpectedCloseDate = expectedCloseDate;
        Stage = stage;
    }

    public void MarkAsLost(string lossReason)
    {
        LossReason = lossReason;
        Stage = "Lost";
    }

    public void UpdateStage(string stage)
    {
        Stage = stage;
    }

    public void SetAmount(decimal amount) => Amount = amount;
}
