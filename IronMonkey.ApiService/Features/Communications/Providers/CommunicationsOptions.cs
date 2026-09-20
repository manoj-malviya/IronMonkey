namespace IronMonkey.ApiService.Features.Communications.Providers;

/// <summary>
/// Provider credentials, read from configuration and secrets.
///
/// <para><b>Providers are platform-wide, not per-tenant.</b> This is a deliberate decision.
/// Per-tenant credentials would mean storing a tenant-supplied API key in a table any tenant
/// Admin can reach, and every endpoint that returns tenant settings then becomes a potential
/// credential leak — a risk the prompt for this feature calls out explicitly. Tenants get
/// their own <em>identity</em> (from-name, reply-to, sender number) without ever holding a
/// secret. A future per-tenant-credential mode would need encryption at rest and a write-only
/// contract, and is not built here.</para>
///
/// <para>Absent credentials disable a channel cleanly: the provider reports
/// <c>IsConfigured == false</c>, sends are rejected with <c>ChannelNotConfigured</c>, and
/// startup is unaffected. Nothing here throws for missing configuration, because a CRM whose
/// API will not boot without an SMS account is worse than one that cannot send SMS.</para>
/// </summary>
public sealed class CommunicationsOptions
{
    public const string SectionName = "Communications";

    public SmtpProviderOptions Smtp { get; set; } = new();
    public TwilioProviderOptions Twilio { get; set; } = new();

    /// <summary>
    /// Forces the no-op provider for every channel regardless of other configuration.
    ///
    /// This exists so a developer with real credentials in their user-secrets cannot
    /// accidentally message a customer from a local run, and so the test suite is guaranteed
    /// never to make a network call.
    /// </summary>
    public bool UseNoopProviders { get; set; }

    /// <summary>
    /// Per-attempt provider timeout. Kept short: a send is retried by Hangfire, so waiting a
    /// long time on a hung provider only delays that retry.
    /// </summary>
    public int ProviderTimeoutSeconds { get; set; } = 15;
}

public sealed class SmtpProviderOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;

    /// <summary>
    /// TLS is on by default. The class this replaces had <c>EnableSsl</c> commented out,
    /// which sent credentials in the clear.
    /// </summary>
    public bool UseSsl { get; set; } = true;

    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }

    /// <summary>A host and a from-address are the minimum to send anything at all.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

public sealed class TwilioProviderOptions
{
    public string? AccountSid { get; set; }
    public string? AuthToken { get; set; }

    /// <summary>E.164 sender number for SMS.</summary>
    public string? FromNumber { get; set; }

    /// <summary>WhatsApp sender, without the "whatsapp:" prefix the API wants.</summary>
    public string? WhatsAppFromNumber { get; set; }

    /// <summary>Overridable so tests can point at a local stub without touching the network.</summary>
    public string BaseUrl { get; set; } = "https://api.twilio.com";

    public bool IsConfiguredForSms =>
        !string.IsNullOrWhiteSpace(AccountSid) && !string.IsNullOrWhiteSpace(AuthToken)
        && !string.IsNullOrWhiteSpace(FromNumber);

    public bool IsConfiguredForWhatsApp =>
        !string.IsNullOrWhiteSpace(AccountSid) && !string.IsNullOrWhiteSpace(AuthToken)
        && !string.IsNullOrWhiteSpace(WhatsAppFromNumber);
}
