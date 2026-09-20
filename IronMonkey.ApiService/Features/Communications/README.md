# Communications

One-to-one conversation history attached to CRM records — email, SMS and WhatsApp. Not a
campaign tool: every message links to a lead or a contact and shows on that record's timeline.

## Configuration

All credentials live in the `Communications` section of configuration, supplied through
environment variables or user-secrets. **Nothing here is tenant-editable**, and no endpoint
returns any of it.

```bash
# Email
Communications__Smtp__Host=smtp.example.com
Communications__Smtp__FromAddress=crm@example.com
Communications__Smtp__Username=...
Communications__Smtp__Password=...

# SMS and WhatsApp
Communications__Twilio__AccountSid=AC...
Communications__Twilio__AuthToken=...
Communications__Twilio__FromNumber=+15550100000
Communications__Twilio__WhatsAppFromNumber=+15550100001

# Webhooks
Communications__WebhookRoutingSecret=<random>     # derives per-tenant callback URLs
Communications__InboundWebhookSecret=<random>     # verifies the generic inbound callback
```

A channel with no credentials is **disabled**, not broken: `GET /api/messages/channels`
reports it unavailable, the UI hides it, and a send against it is recorded as
`Rejected` / `ChannelNotConfigured`. The API starts normally either way.

For local development, `Communications:UseNoopProviders` (the default in `appsettings.json`)
records sends without performing them, so a stray credential cannot message a real customer.
The test suite never needs a credential and never makes a network call.

## Webhook URLs

Each tenant has its own callback URL. The token is derived from the tenant id and
`WebhookRoutingSecret` — it is a routing identifier, not an authenticator, since the provider
signature is what authenticates the caller.

```
POST /api/webhooks/twilio/{routingToken}      # inbound SMS/WhatsApp + delivery receipts
POST /api/webhooks/inbound/{routingToken}     # generic signed callback (email providers)
```

A tenant Admin gets their own paths from `GET /api/messages/webhook-urls` (requires
`settings:write`); prefix them with the deployment's public origin.

Twilio callbacks are verified with Twilio's own HMAC-SHA1 scheme over the full request URL,
so the URL registered in the Twilio console must match what the API sees — behind a reverse
proxy that means forwarded headers have to be honoured, or every callback fails its signature
check.

The generic endpoint expects:

```
X-IronMonkey-Timestamp: <unix seconds>
X-IronMonkey-Signature: <hex HMAC-SHA256 of "{timestamp}.{body}" keyed by InboundWebhookSecret>
```

Timestamps more than five minutes from now are rejected as replays.

## Adding a provider

Implement `IMessageProvider`, declare the `Channel` it serves, and return `false` from
`IsConfigured` when its credentials are absent. Register it in
`ConfigureServices.AddCommunications`. The registry indexes only configured providers, and
the last registration for a channel wins.

Classify failures carefully: `SendResult.Transient` is retried by Hangfire, `SendResult.Permanent`
is not. Retrying a malformed recipient or a rejected credential only delays the failure the
user needs to see.
