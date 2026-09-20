using IronMonkey.Data.Abstractions;
using IronMonkey.Data.Communications;

namespace IronMonkey.Data.Entities;

/// <summary>
/// One recipient's opt-out for one channel.
///
/// Keyed by the address itself (email or E.164 number) rather than by lead or contact id,
/// deliberately: the same person is routinely both a lead and a contact, and may be several
/// leads after a duplicate import. Someone who replies STOP has opted that <em>number</em>
/// out, and keying on a record id would let the next duplicate of them be messaged again.
///
/// Rows exist only for suppression. Absence means "no opt-out recorded", which is the
/// default and is what permits a send — so a failure to read this table can never silently
/// turn into permission to message someone.
/// </summary>
public sealed class MessageConsent : BaseTenantEntity
{
    private MessageConsent() { }

    public MessageChannel Channel { get; private set; }

    /// <summary>
    /// The normalized recipient address this applies to — lowercased email, or digits-only
    /// phone number. Normalization happens in <c>ConsentAddress</c> so that the value written
    /// by an unsubscribe link and the value checked at send time cannot diverge.
    /// </summary>
    public string Address { get; private set; } = string.Empty;

    public bool IsOptedOut { get; private set; }

    /// <summary>How the opt-out arrived: "StopKeyword", "UnsubscribeLink", "Manual", "Bounce".</summary>
    public string Source { get; private set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; private set; }

    public static MessageConsent OptOut(Guid tenantId, MessageChannel channel, string normalizedAddress,
        string source, DateTime whenUtc)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Channel = channel,
            Address = normalizedAddress,
            IsOptedOut = true,
            Source = source,
            UpdatedAtUtc = whenUtc
        };

    /// <summary>
    /// Records an explicit opt back in. The row is kept rather than deleted so that the
    /// history of a customer's consent decisions survives — which is what a compliance
    /// question actually asks about.
    /// </summary>
    public void OptIn(string source, DateTime whenUtc)
    {
        IsOptedOut = false;
        Source = source;
        UpdatedAtUtc = whenUtc;
    }

    public void OptOutAgain(string source, DateTime whenUtc)
    {
        IsOptedOut = true;
        Source = source;
        UpdatedAtUtc = whenUtc;
    }
}
