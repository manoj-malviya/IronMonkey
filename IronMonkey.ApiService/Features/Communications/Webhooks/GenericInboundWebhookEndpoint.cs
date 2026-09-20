using System.Text.Json;
using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.ApiService.Common;
using IronMonkey.Data;
using IronMonkey.Data.Communications;

namespace IronMonkey.ApiService.Features.Communications.Webhooks;

/// <summary>
/// Generic signed inbound endpoint, for email providers and anything following the common
/// <c>t=timestamp,v1=hmac</c> convention.
///
/// Same rules as the Twilio callback and for the same reasons: the signature authenticates,
/// the routing token in the path selects the tenant, and a body-supplied tenant id is ignored
/// entirely. It additionally rejects replays on the signed timestamp, so a captured valid
/// callback cannot be resubmitted to re-inject a message.
/// </summary>
public class GenericInboundWebhookEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/webhooks/inbound/{routingToken}", Handle)
        .WithSummary("Signed inbound message callback")
        .WithTags("Communications")
        .AllowAnonymous()
        .ExcludeFromDescription();

    /// <summary>
    /// Cap on a callback body. Without it, an unauthenticated endpoint will happily buffer
    /// whatever it is sent.
    /// </summary>
    private const int MaxBodyBytes = 256 * 1024;

    internal static async Task<IResult> Handle(
        string routingToken,
        HttpContext httpContext,
        IWebhookTenantResolver tenantResolver,
        ITenantRegistry tenantRegistry,
        ITenantDbContextFactory dbContextFactory,
        IInboundMessageService inboundMessages,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<GenericInboundWebhookEndpoint> logger,
        CancellationToken cancellationToken)
    {
        httpContext.Request.EnableBuffering();

        if (httpContext.Request.ContentLength > MaxBodyBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        using var reader = new StreamReader(httpContext.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);

        if (body.Length > MaxBodyBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        var timestamp = httpContext.Request.Headers["X-IronMonkey-Timestamp"].ToString();
        var signature = httpContext.Request.Headers["X-IronMonkey-Signature"].ToString();
        var secret = configuration["Communications:InboundWebhookSecret"];

        if (!WebhookSignatureVerifier.VerifyHmacSha256(secret, timestamp, body, signature, timeProvider.GetUtcNow()))
        {
            logger.LogWarning("Rejected an inbound callback with an invalid or expired signature");
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var tenantId = await tenantResolver.ResolveAsync(routingToken, cancellationToken);
        if (tenantId is null)
            return Results.StatusCode(StatusCodes.Status404NotFound);

        InboundPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<InboundPayload>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.From))
            return Results.BadRequest();

        if (!Enum.TryParse<MessageChannel>(payload.Channel, ignoreCase: true, out var channel)
            || !Enum.IsDefined(channel))
        {
            return Results.BadRequest();
        }

        var connectionString = await tenantRegistry.GetConnectionStringAsync(tenantId.Value, cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId.Value);

        // A delivery receipt and an inbound message arrive on the same endpoint, distinguished
        // by which fields are present — a receipt names a message, a message names a sender.
        if (!string.IsNullOrWhiteSpace(payload.ProviderMessageId) && !string.IsNullOrWhiteSpace(payload.Status))
        {
            var mapped = MapStatus(payload.Status);
            if (mapped is not null)
            {
                await inboundMessages.ApplyDeliveryReceiptAsync(
                    db, payload.ProviderMessageId!, mapped.Value, payload.Reason, cancellationToken);
            }

            return Results.Ok();
        }

        await inboundMessages.RecordAsync(db, tenantId.Value, new InboundMessage(
            channel,
            string.IsNullOrWhiteSpace(payload.Provider) ? "Inbound" : payload.Provider!,
            payload.ProviderMessageId,
            payload.From!,
            payload.To ?? string.Empty,
            payload.Subject,
            payload.Body ?? string.Empty), cancellationToken);

        return Results.Ok();
    }

    private static MessageStatus? MapStatus(string status) => status.ToLowerInvariant() switch
    {
        "delivered" => MessageStatus.Delivered,
        "bounced" or "bounce" => MessageStatus.Bounced,
        "failed" => MessageStatus.Failed,
        _ => null
    };

    /// <summary>
    /// The accepted callback shape. Note there is deliberately no TenantId property — the
    /// tenant comes from the routing token, and accepting one here would reintroduce exactly
    /// the cross-tenant write this design prevents.
    /// </summary>
    internal sealed record InboundPayload(
        string? Channel,
        string? Provider,
        string? ProviderMessageId,
        string? From,
        string? To,
        string? Subject,
        string? Body,
        string? Status,
        string? Reason);
}
