using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Communications.Webhooks;
using IronMonkey.Common.Auth;

namespace IronMonkey.ApiService.Features.Communications.Endpoints;

public sealed record WebhookUrls(string TwilioCallbackPath, string InboundCallbackPath);

/// <summary>
/// The current tenant's provider callback paths, so an Admin can paste them into a provider
/// console without an operator deriving the token by hand.
///
/// Returns paths rather than absolute URLs: the public host depends on the deployment and any
/// reverse proxy in front of it, and guessing it here would hand out a URL whose signature
/// check then fails. It returns no credential — the routing token only selects a tenant, and a
/// callback still has to carry a valid provider signature to be accepted.
///
/// Gated on settings:write rather than messages:read: the token is not a secret, but it is
/// infrastructure configuration, and there is no reason for every user who can read a
/// conversation to see it.
/// </summary>
public class GetWebhookUrlsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/messages/webhook-urls", Handle)
        .WithSummary("Get this tenant's provider callback paths")
        .WithTags("Communications")
        .RequireAuthorization(PermissionConstants.SettingsWrite);

    internal static Ok<WebhookUrls> Handle(
        ITenantService tenantService,
        IWebhookTenantResolver resolver)
    {
        // The tenant comes from the claim, never from a parameter — otherwise this would hand
        // any authenticated user another tenant's callback path.
        var token = resolver.TokenFor(tenantService.GetCurrentTenantId());

        return TypedResults.Ok(new WebhookUrls(
            $"/api/webhooks/twilio/{token}",
            $"/api/webhooks/inbound/{token}"));
    }
}
