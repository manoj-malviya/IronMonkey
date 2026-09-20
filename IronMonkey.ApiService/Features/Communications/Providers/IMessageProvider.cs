using IronMonkey.Data.Communications;

namespace IronMonkey.ApiService.Features.Communications.Providers;

/// <summary>
/// What a provider is asked to send. Channel-specific fields are present but nullable rather
/// than split across three interfaces: a provider declares the channel it serves, so it only
/// ever reads the fields that apply to it, while the dispatcher stays one code path.
/// </summary>
public sealed record OutboundMessageRequest(
    MessageChannel Channel,
    string From,
    string To,
    string? Subject,
    string Body,
    bool IsBodyHtml,
    /// <summary>
    /// Carried through to the provider where the provider supports it, so a delivery receipt
    /// and a retry can both be tied back to the same row without trusting the provider to
    /// invent a stable id.
    /// </summary>
    string IdempotencyKey,
    /// <summary>WhatsApp: the approved template name to send outside the service window.</summary>
    string? ProviderTemplateName = null);

/// <summary>
/// The outcome of handing one message to a provider.
///
/// There is no "probably worked" state. A provider either returns an acceptance with an id,
/// or a categorized failure — which is what stops this layer from repeating the defect in the
/// class it replaces, where a fire-and-forget send was logged as success unconditionally.
/// </summary>
public sealed record SendResult
{
    private SendResult() { }

    public bool Accepted { get; private init; }

    /// <summary>The provider's id for the message. Present only on acceptance.</summary>
    public string? ProviderMessageId { get; private init; }

    public MessageErrorCategory ErrorCategory { get; private init; } = MessageErrorCategory.None;

    /// <summary>Raw failure detail. The caller redacts this before persisting it.</summary>
    public string? ErrorMessage { get; private init; }

    /// <summary>
    /// Whether another attempt could plausibly succeed. A timeout is retryable; a malformed
    /// recipient or a rejected credential is not, and retrying it just delays the failure
    /// while burning quota.
    /// </summary>
    public bool IsRetryable { get; private init; }

    public static SendResult Success(string? providerMessageId) =>
        new() { Accepted = true, ProviderMessageId = providerMessageId };

    /// <summary>A failure worth retrying: network, timeout, provider 5xx.</summary>
    public static SendResult Transient(MessageErrorCategory category, string? message) =>
        new() { Accepted = false, ErrorCategory = category, ErrorMessage = message, IsRetryable = true };

    /// <summary>A failure that retrying cannot fix: bad recipient, rejected credentials, 4xx.</summary>
    public static SendResult Permanent(MessageErrorCategory category, string? message) =>
        new() { Accepted = false, ErrorCategory = category, ErrorMessage = message, IsRetryable = false };
}

/// <summary>
/// One channel's outbound transport.
///
/// Providers are resolved by <see cref="Channel"/> at send time. A channel with no registered
/// provider is not an error at startup — it is a channel the tenant cannot use, reported as
/// such — which is the same "absent credentials disable the feature cleanly" rule the
/// PlatformAdmin seeder follows.
/// </summary>
public interface IMessageProvider
{
    MessageChannel Channel { get; }

    /// <summary>Provider name recorded on the message row, e.g. "Smtp", "Twilio", "Noop".</summary>
    string Name { get; }

    /// <summary>
    /// Whether this provider has everything it needs to send.
    ///
    /// False when credentials are absent. A half-configured provider must report false rather
    /// than attempting a send that fails at the network layer: the resulting message row then
    /// says ChannelNotConfigured, which an Admin can act on, instead of a connection error
    /// that reads like an outage.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>The address messages are sent from, for the message row's From column.</summary>
    string FromAddress { get; }

    Task<SendResult> SendAsync(OutboundMessageRequest request, CancellationToken cancellationToken);
}
