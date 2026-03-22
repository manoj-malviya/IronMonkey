using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public sealed class Notification : BaseTenantEntity
{
    private Notification() { }

    public Guid RecipientUserId { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public bool IsRead { get; private set; } = false;
    public Guid? LeadId { get; private set; }

    public static Notification Create(Guid tenantId, Guid recipientUserId, string message, Guid? leadId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RecipientUserId = recipientUserId,
            Message = message,
            LeadId = leadId
        };

    public void MarkAsRead() => IsRead = true;
}
