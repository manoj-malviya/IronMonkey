using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using IronMonkey.ApiService.Features.Communications.Providers;
using IronMonkey.Data.Communications;
using Xunit;

namespace IronMonkey.Tests.Unit;

/// <summary>
/// Provider configuration and the registry.
///
/// The rule these pin: absent credentials disable a channel <em>cleanly</em>. Nothing throws
/// at startup, and an unconfigured provider is indistinguishable from an absent one at the
/// call site — so a caller can never accidentally attempt a send through a half-configured
/// provider and get a connection error that reads like an outage.
/// </summary>
public class CommunicationsConfigurationTests
{
    private static IOptions<CommunicationsOptions> Options(CommunicationsOptions options) =>
        Microsoft.Extensions.Options.Options.Create(options);

    [Fact]
    public void Smtp_without_a_host_is_not_configured()
    {
        var provider = new SmtpMessageProvider(
            Options(new CommunicationsOptions { Smtp = { FromAddress = "crm@example.com" } }),
            NullLogger<SmtpMessageProvider>.Instance);

        Assert.False(provider.IsConfigured);
    }

    [Fact]
    public void Smtp_without_a_from_address_is_not_configured()
    {
        var provider = new SmtpMessageProvider(
            Options(new CommunicationsOptions { Smtp = { Host = "smtp.example.com" } }),
            NullLogger<SmtpMessageProvider>.Instance);

        Assert.False(provider.IsConfigured);
    }

    [Fact]
    public void Smtp_with_a_host_and_a_from_address_is_configured()
    {
        var provider = new SmtpMessageProvider(
            Options(new CommunicationsOptions
            {
                Smtp = { Host = "smtp.example.com", FromAddress = "crm@example.com" }
            }),
            NullLogger<SmtpMessageProvider>.Instance);

        Assert.True(provider.IsConfigured);
    }

    [Fact]
    public void Smtp_defaults_to_tls()
    {
        // The class this replaces had EnableSsl commented out, which sent credentials in the
        // clear. Defaulting on means a deployment has to opt out deliberately.
        Assert.True(new SmtpProviderOptions().UseSsl);
    }

    [Fact]
    public async Task An_unconfigured_smtp_provider_refuses_rather_than_attempting_a_connection()
    {
        var provider = new SmtpMessageProvider(
            Options(new CommunicationsOptions()), NullLogger<SmtpMessageProvider>.Instance);

        var result = await provider.SendAsync(
            new OutboundMessageRequest(MessageChannel.Email, "a@b.com", "c@d.com", "s", "b", true, "key"),
            default);

        Assert.False(result.Accepted);
        Assert.Equal(MessageErrorCategory.ChannelNotConfigured, result.ErrorCategory);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public void Twilio_needs_a_sid_token_and_number_per_channel()
    {
        var partial = new TwilioProviderOptions { AccountSid = "AC123", AuthToken = "token" };

        // Credentials alone are not enough — a sender number is channel-specific, and SMS
        // being configured says nothing about WhatsApp.
        Assert.False(partial.IsConfiguredForSms);
        Assert.False(partial.IsConfiguredForWhatsApp);

        partial.FromNumber = "+15550100000";
        Assert.True(partial.IsConfiguredForSms);
        Assert.False(partial.IsConfiguredForWhatsApp);

        partial.WhatsAppFromNumber = "+15550100001";
        Assert.True(partial.IsConfiguredForWhatsApp);
    }

    [Fact]
    public void Constructing_a_twilio_provider_for_email_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TwilioMessageProvider(
            MessageChannel.Email, null!, Options(new CommunicationsOptions()),
            NullLogger<TwilioMessageProvider>.Instance));
    }

    [Fact]
    public void The_registry_indexes_only_configured_providers()
    {
        var registry = new MessageProviderRegistry([
            new StubProvider(MessageChannel.Email, isConfigured: true),
            new StubProvider(MessageChannel.Sms, isConfigured: false)
        ]);

        Assert.NotNull(registry.For(MessageChannel.Email));

        // An unconfigured provider is absent as far as any caller is concerned.
        Assert.Null(registry.For(MessageChannel.Sms));
        Assert.Null(registry.For(MessageChannel.WhatsApp));

        Assert.Equal([MessageChannel.Email], registry.AvailableChannels());
    }

    [Fact]
    public void An_empty_registry_is_valid_and_reports_no_channels()
    {
        // A deployment with no messaging configured at all must construct fine — the feature
        // is disabled, not broken.
        var registry = new MessageProviderRegistry([]);

        Assert.Empty(registry.AvailableChannels());
        foreach (var channel in Enum.GetValues<MessageChannel>())
            Assert.Null(registry.For(channel));
    }

    [Fact]
    public void The_last_registration_for_a_channel_wins()
    {
        // This is what lets the no-op provider override a real one when UseNoopProviders is set.
        var real = new StubProvider(MessageChannel.Email, true, "Real");
        var noop = new StubProvider(MessageChannel.Email, true, "Noop");

        var registry = new MessageProviderRegistry([real, noop]);

        Assert.Equal("Noop", registry.For(MessageChannel.Email)!.Name);
    }

    [Fact]
    public async Task The_noop_provider_accepts_without_sending_and_needs_no_credentials()
    {
        // This is what makes the test suite run with no provider credentials and no network.
        var provider = new NoopMessageProvider(MessageChannel.Email, NullLogger<NoopMessageProvider>.Instance);

        Assert.True(provider.IsConfigured);

        var result = await provider.SendAsync(
            new OutboundMessageRequest(MessageChannel.Email, "a@b.com", "c@d.com", "s", "b", true, "stable-key"),
            default);

        Assert.True(result.Accepted);

        // Derived from the key rather than random, so a replayed send in a test produces a
        // stable id and duplicate assertions stay meaningful.
        Assert.Equal("noop-stable-key", result.ProviderMessageId);
    }

    private sealed class StubProvider(MessageChannel channel, bool isConfigured, string name = "Stub")
        : IMessageProvider
    {
        public MessageChannel Channel { get; } = channel;
        public string Name { get; } = name;
        public bool IsConfigured { get; } = isConfigured;
        public string FromAddress => "stub";

        public Task<SendResult> SendAsync(OutboundMessageRequest request, CancellationToken cancellationToken)
            => Task.FromResult(SendResult.Success("stub"));
    }
}
