using Microsoft.Extensions.Options;
using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Features.Communications.Providers;
using IronMonkey.Data;
using IronMonkey.Data.Communications;

namespace IronMonkey.ApiService.Features.Communications.Webhooks;

/// <summary>
/// Receives Twilio inbound messages and delivery receipts.
///
/// <para>Anonymous by necessity — Twilio has no session — so the signature is the
/// authentication and an unsigned or mis-signed callback is rejected with 403 before anything
/// is read from it. The tenant comes from the routing token in the path, never from the body.
/// A body carrying its own tenant id would let any caller write into any tenant.</para>
///
/// <para>Always returns 200 once the callback is verified, even when the message cannot be
/// matched to a record. Twilio retries non-2xx responses, and retrying does not help a message
/// whose sender is genuinely unknown — the message is stored in the unmatched queue instead.</para>
/// </summary>
public class TwilioWebhookEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/webhooks/twilio/{routingToken}", Handle)
        .WithSummary("Twilio inbound message and delivery status callback")
        .WithTags("Communications")
        .AllowAnonymous()
        // Excluded from the OpenAPI document: it is a provider-to-server contract, not part
        // of the tenant API surface, and publishing it only advertises the callback URL shape.
        .ExcludeFromDescription();

    internal static async Task<IResult> Handle(
        string routingToken,
        HttpContext httpContext,
        IWebhookTenantResolver tenantResolver,
        ITenantRegistry tenantRegistry,
        ITenantDbContextFactory dbContextFactory,
        IInboundMessageService inboundMessages,
        IOptions<CommunicationsOptions> options,
        ILogger<TwilioWebhookEndpoint> logger,
        CancellationToken cancellationToken)
    {
        if (!httpContext.Request.HasFormContentType)
            return Results.BadRequest();

        var form = await httpContext.Request.ReadFormAsync(cancellationToken);

        var formPairs = form
            .Select(field => new KeyValuePair<string, string>(field.Key, field.Value.ToString()))
            .ToList();

        var signature = httpContext.Request.Headers["X-Twilio-Signature"].ToString();
        var url = BuildRequestUrl(httpContext);

        if (!WebhookSignatureVerifier.VerifyTwilio(options.Value.Twilio.AuthToken, url, formPairs, signature))
        {
            // Logged without the signature or body: a rejected callback is exactly where a
            // forged credential would appear, and this line is written to shared operator logs.
            logger.LogWarning("Rejected a Twilio callback with an invalid signature");
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // Resolved only after the signature passes, so an unverified caller cannot use response
        // timing to discover which routing tokens are real.
        var tenantId = await tenantResolver.ResolveAsync(routingToken, cancellationToken);

        if (tenantId is null)
        {
            logger.LogWarning("Twilio callback carried a routing token that matches no tenant");
            return Results.StatusCode(StatusCodes.Status404NotFound);
        }

        var connectionString = await tenantRegistry.GetConnectionStringAsync(tenantId.Value, cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId.Value);

        var messageStatus = form["MessageStatus"].ToString();

        // A callback carrying MessageStatus is a delivery receipt for a message we sent; one
        // carrying Body is an inbound message from the customer.
        if (!string.IsNullOrWhiteSpace(messageStatus))
        {
            var sid = form["MessageSid"].ToString();
            var mapped = MapStatus(messageStatus);

            if (mapped is not null && !string.IsNullOrWhiteSpace(sid))
            {
                await inboundMessages.ApplyDeliveryReceiptAsync(
                    db, sid, mapped.Value, form["ErrorMessage"].ToString(), cancellationToken);
            }

            return Results.Ok();
        }

        var from = form["From"].ToString();
        var to = form["To"].ToString();

        if (string.IsNullOrWhiteSpace(from))
            return Results.Ok();

        // WhatsApp addresses arrive scheme-prefixed; stripping it here means one stored form
        // for a number regardless of which channel it came in on.
        var channel = from.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase)
            ? MessageChannel.WhatsApp
            : MessageChannel.Sms;

        await inboundMessages.RecordAsync(db, tenantId.Value, new InboundMessage(
            channel,
            "Twilio",
            form["MessageSid"].ToString(),
            StripScheme(from),
            StripScheme(to),
            Subject: null,
            form["Body"].ToString()), cancellationToken);

        return Results.Ok();
    }

    private static string StripScheme(string address) =>
        address.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase) ? address["whatsapp:".Length..] : address;

    /// <summary>
    /// The absolute URL Twilio signed.
    ///
    /// Rebuilt from the request rather than from configuration, because the signature covers
    /// the exact URL Twilio called — including scheme and host. Behind a reverse proxy this
    /// depends on forwarded headers being honoured, which is why a mismatch shows up as a
    /// signature failure rather than silently passing.
    /// </summary>
    private static string BuildRequestUrl(HttpContext httpContext)
    {
        var request = httpContext.Request;
        return $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}{request.QueryString}";
    }

    /// <summary>
    /// Maps Twilio's status vocabulary onto the lifecycle.
    ///
    /// "sent" and "queued" are deliberately not mapped: they describe a state the row is
    /// already in, and writing them would let a late-arriving callback move a delivered
    /// message backwards.
    /// </summary>
    private static MessageStatus? MapStatus(string status) => status.ToLowerInvariant() switch
    {
        "delivered" => MessageStatus.Delivered,
        "undelivered" => MessageStatus.Bounced,
        "failed" => MessageStatus.Failed,
        _ => null
    };
}
