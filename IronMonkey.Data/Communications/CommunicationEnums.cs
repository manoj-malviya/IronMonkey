namespace IronMonkey.Data.Communications;

/// <summary>
/// The channels a message can travel on.
///
/// Deliberately a closed enum rather than a string: every capability decision in this
/// feature — whether a subject exists, whether a template is required, which consent row
/// applies, which quiet-hours window is checked — switches on this value, and a free-text
/// channel would make each of those silently fall through to a default.
/// </summary>
public enum MessageChannel
{
    Email = 0,
    Sms = 1,
    WhatsApp = 2
}

/// <summary>Who started the conversation turn.</summary>
public enum MessageDirection
{
    /// <summary>Sent by the tenant to the customer.</summary>
    Outbound = 0,

    /// <summary>Received from the customer.</summary>
    Inbound = 1
}

/// <summary>
/// Where an outbound message has actually got to.
///
/// This is an explicit lifecycle rather than a boolean, because the bug this feature
/// replaces was precisely the inference that "no exception" means "delivered". Nothing
/// advances past <see cref="Sent"/> except a provider telling us so: <see cref="Delivered"/>
/// and <see cref="Bounced"/> are only ever written by a verified delivery receipt.
/// </summary>
public enum MessageStatus
{
    /// <summary>Persisted and enqueued. No provider has seen it yet.</summary>
    Queued = 0,

    /// <summary>
    /// A provider accepted it. This is an acknowledgement of handover, not of delivery —
    /// a provider that accepts a message can still fail to deliver it.
    /// </summary>
    Sent = 1,

    /// <summary>The provider confirmed delivery to the recipient.</summary>
    Delivered = 2,

    /// <summary>The recipient's server or handset rejected it after acceptance.</summary>
    Bounced = 3,

    /// <summary>
    /// The send could not be completed — network failure, timeout, or a provider error
    /// that may be transient. Retryable.
    /// </summary>
    Failed = 4,

    /// <summary>
    /// The send was refused before or by the provider and will not be retried: the channel
    /// is not configured, the recipient is suppressed, the address is malformed, or the
    /// provider returned a permanent 4xx. Distinct from <see cref="Failed"/> because
    /// retrying it can never help.
    /// </summary>
    Rejected = 5
}

/// <summary>
/// Why a send did not succeed, in categories an operator can act on.
///
/// Mirrors the reasoning behind <c>WorkflowErrorCategory</c>: the category is what gets
/// filtered and grouped, while the message carries redacted detail, so rewording a message
/// never breaks a saved filter. Crucially, this distinguishes the three failures the prompt
/// calls out — provider failure, timeout, and non-2xx — which a single "error" flag conflates.
/// </summary>
public enum MessageErrorCategory
{
    None = 0,

    /// <summary>No provider is configured for this channel, so nothing was attempted.</summary>
    ChannelNotConfigured = 1,

    /// <summary>The recipient address or number was absent or malformed.</summary>
    InvalidRecipient = 2,

    /// <summary>The recipient has opted out of this channel. Never retried.</summary>
    RecipientSuppressed = 3,

    /// <summary>The provider host could not be reached.</summary>
    ProviderNetworkFailure = 4,

    /// <summary>The provider did not respond before the timeout elapsed.</summary>
    ProviderTimeout = 5,

    /// <summary>
    /// The provider responded with a non-2xx status. Split from the two above because it
    /// means the provider was reachable and chose to refuse — usually a credential,
    /// quota or payload problem rather than an infrastructure one.
    /// </summary>
    ProviderRejected = 6,

    /// <summary>The message violates a channel rule (length, missing template, outside window).</summary>
    ChannelRuleViolation = 7,

    /// <summary>The send was deferred or refused because it falls outside the tenant's quiet hours.</summary>
    QuietHours = 8,

    /// <summary>The tenant exceeded its configured send rate for this channel.</summary>
    RateLimited = 9,

    /// <summary>A template referenced a field that no longer exists, or failed to render.</summary>
    TemplateRenderFailure = 10,

    /// <summary>Something unanticipated. The catch-all, kept last.</summary>
    UnexpectedError = 99
}
