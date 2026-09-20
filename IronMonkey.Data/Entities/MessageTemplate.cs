using IronMonkey.Data.Abstractions;
using IronMonkey.Data.Communications;

namespace IronMonkey.Data.Entities;

/// <summary>
/// A tenant-editable message body with placeholders.
///
/// Templates are validated against the tenant's real field definitions when saved, so a
/// placeholder naming a field that does not exist is caught by the person editing it rather
/// than rendering blank in a customer's inbox at 3am.
/// </summary>
public sealed class MessageTemplate : BaseTenantEntity
{
    private MessageTemplate() { }

    public string Name { get; private set; } = string.Empty;
    public MessageChannel Channel { get; private set; }

    /// <summary>Email only; null for SMS and WhatsApp.</summary>
    public string? Subject { get; private set; }

    public string Body { get; private set; } = string.Empty;

    /// <summary>
    /// WhatsApp only: the provider-side approved template name. WhatsApp refuses free-form
    /// messages outside the 24-hour customer-service window, so a template that cannot name
    /// its approved counterpart is unusable for anything but a reply.
    /// </summary>
    public string? ProviderTemplateName { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static MessageTemplate Create(Guid tenantId, string name, MessageChannel channel,
        string? subject, string body, string? providerTemplateName = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Channel = channel,
            Subject = channel == MessageChannel.Email ? subject : null,
            Body = body,
            ProviderTemplateName = channel == MessageChannel.WhatsApp ? providerTemplateName : null
        };

    public void Update(string name, string? subject, string body, string? providerTemplateName)
    {
        Name = name;
        Subject = Channel == MessageChannel.Email ? subject : null;
        Body = body;
        ProviderTemplateName = Channel == MessageChannel.WhatsApp ? providerTemplateName : null;
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}
