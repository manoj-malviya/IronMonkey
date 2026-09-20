using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using IronMonkey.Data.Communications;

namespace IronMonkey.ApiService.Features.Communications.Providers;

/// <summary>
/// Sends SMS and WhatsApp through Twilio's REST API.
///
/// One class serves both channels because Twilio's Messages resource is the same for each —
/// WhatsApp differs only in the <c>whatsapp:</c> address prefix. Two instances are registered,
/// one per channel, so provider resolution stays a simple lookup by channel and a tenant with
/// SMS configured but not WhatsApp gets an accurate "not configured" for the latter.
///
/// The HTTP client is named and injected rather than constructed per call — the class this
/// replaces built an SmtpClient on every send, which exhausts sockets under load.
/// </summary>
public sealed class TwilioMessageProvider : IMessageProvider
{
    /// <summary>Named client so this gets its own timeout and carries no ambient auth.</summary>
    public const string HttpClientName = "TwilioMessaging";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CommunicationsOptions _options;
    private readonly ILogger<TwilioMessageProvider> _logger;

    public TwilioMessageProvider(
        MessageChannel channel,
        IHttpClientFactory httpClientFactory,
        IOptions<CommunicationsOptions> options,
        ILogger<TwilioMessageProvider> logger)
    {
        if (channel is not (MessageChannel.Sms or MessageChannel.WhatsApp))
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Twilio serves SMS and WhatsApp only.");

        Channel = channel;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    private TwilioProviderOptions Twilio => _options.Twilio;

    public MessageChannel Channel { get; }

    public string Name => "Twilio";

    public bool IsConfigured => Channel == MessageChannel.Sms
        ? Twilio.IsConfiguredForSms
        : Twilio.IsConfiguredForWhatsApp;

    public string FromAddress => (Channel == MessageChannel.Sms
        ? Twilio.FromNumber
        : Twilio.WhatsAppFromNumber) ?? string.Empty;

    public async Task<SendResult> SendAsync(OutboundMessageRequest request, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return SendResult.Permanent(MessageErrorCategory.ChannelNotConfigured,
                $"Twilio is not configured for {Channel}.");

        if (string.IsNullOrWhiteSpace(request.To))
            return SendResult.Permanent(MessageErrorCategory.InvalidRecipient, "No recipient number.");

        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(_options.ProviderTimeoutSeconds);

        var url = $"{Twilio.BaseUrl.TrimEnd('/')}/2010-04-01/Accounts/{Twilio.AccountSid}/Messages.json";

        var form = new List<KeyValuePair<string, string>>
        {
            new("To", AddressFor(request.To)),
            new("From", AddressFor(FromAddress)),
            new("Body", request.Body)
        };

        using var content = new FormUrlEncodedContent(form);
        using var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

        // Basic auth per request rather than on the shared client: the named client is a
        // singleton across the app, and mutating its default headers would leak credentials
        // into any other use of the same client name.
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{Twilio.AccountSid}:{Twilio.AuthToken}"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        // Twilio honours this header by returning the original message on a replay rather
        // than creating a second one — so a Hangfire retry that reaches the provider still
        // cannot bill or deliver twice.
        message.Headers.TryAddWithoutValidation("I-Twilio-Idempotency-Token", request.IdempotencyKey);

        try
        {
            using var response = await client.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                return ClassifyHttpFailure(response.StatusCode, body);

            return SendResult.Success(ReadMessageSid(body) ?? request.IdempotencyKey);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient surfaces its own timeout as TaskCanceledException. Distinguished
            // from a real shutdown cancellation by checking the token, so a timeout is not
            // misreported as the worker stopping.
            _logger.LogWarning("Twilio {Channel} send timed out for message {Key}", Channel, request.IdempotencyKey);
            return SendResult.Transient(MessageErrorCategory.ProviderTimeout, "Twilio did not respond before the timeout.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SendResult.Transient(MessageErrorCategory.ProviderTimeout, "The send was cancelled before it completed.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Twilio {Channel} send failed to reach the provider for message {Key}",
                Channel, request.IdempotencyKey);
            return SendResult.Transient(MessageErrorCategory.ProviderNetworkFailure, ex.Message);
        }
    }

    /// <summary>WhatsApp addresses carry a scheme prefix; SMS numbers are bare.</summary>
    private string AddressFor(string number) =>
        Channel == MessageChannel.WhatsApp && !number.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase)
            ? $"whatsapp:{number}"
            : number;

    /// <summary>
    /// Splits provider refusals from provider outages.
    ///
    /// 429 and 5xx clear on their own, so they are retried. A 4xx means Twilio understood the
    /// request and refused it — a bad number, a suspended account, an unapproved template —
    /// and the same request will be refused identically next time.
    /// </summary>
    private static SendResult ClassifyHttpFailure(HttpStatusCode status, string body)
    {
        var detail = ReadErrorMessage(body) ?? $"Twilio returned {(int)status}.";

        if (status == HttpStatusCode.TooManyRequests)
            return SendResult.Transient(MessageErrorCategory.RateLimited, detail);

        if ((int)status >= 500)
            return SendResult.Transient(MessageErrorCategory.ProviderRejected, detail);

        return SendResult.Permanent(MessageErrorCategory.ProviderRejected, detail);
    }

    private static string? ReadMessageSid(string body) => ReadStringProperty(body, "sid");

    private static string? ReadErrorMessage(string body) => ReadStringProperty(body, "message");

    /// <summary>
    /// Reads one property from a provider response without throwing on a body that is not
    /// JSON — a gateway returning an HTML error page must not turn a send failure into an
    /// unhandled exception in the job.
    /// </summary>
    private static string? ReadStringProperty(string body, string property)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty(property, out var value)
                   && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
