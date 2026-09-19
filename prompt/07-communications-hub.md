# Communications Hub — Email, SMS and WhatsApp

## Context

Every industry this CRM targets runs on conversations with leads, but the system cannot currently hold one. There is no outbound messaging worth the name:

- `EmailService` sends a hardcoded "writer invitation" from another product, with a commented-out `EnableSsl` and a `SmtpClient` built per call.
- `EmailSender` posts to `localhost:25` with a hardcoded `test@maoj.com` sender, fires `SendAsync` without awaiting it, and then logs success unconditionally via `true ? ... : ...` — it reports delivery it never confirmed.
- The workflow engine's email action is documented as "not delivered" and only logs.
- `Notification` is a tenant row with a message string and an `IsRead` flag; nothing delivers it anywhere.

Both email classes are dead code from a different application and should be removed, not extended. `PROJECT.md` lists email, SMS and WhatsApp as required constraints.

## Prompt

Build a provider-agnostic communications layer that records every message against the lead or contact it concerns, and wire the workflow engine's existing email action into it.

### Provider abstraction

- Define one outbound-message abstraction covering email, SMS and WhatsApp, with per-channel capabilities rather than a lowest-common-denominator interface — email has a subject and HTML body, SMS has a segment count and a hard length limit, WhatsApp requires pre-approved templates outside its customer-service window.
- Support at least one real provider per channel plus a no-op/logging provider for local development and tests. No test may require live provider credentials or make a network call.
- Read credentials from configuration and secrets, never from tenant-editable data or source. Follow the existing `PlatformAdmin` pattern: absent credentials must disable the channel cleanly rather than half-configure it or crash at startup.
- Decide and document explicitly whether providers are platform-wide or per-tenant (a tenant sending from its own domain or number). If per-tenant credentials are stored, encrypt them at rest and never return them from any API — a `GET` that echoes back a stored API key is a credential leak to every Admin in the tenant.

### Message model and threading

- Add a tenant-scoped message entity recording channel, direction, provider, provider message id, from/to, subject, body reference, status, timestamps, error category and safe error message, and links to the lead/contact and the sending user or workflow rule.
- Model status as an explicit lifecycle — queued, sent, delivered, bounced, failed, rejected — and update it from provider callbacks where the provider supports them. Do not infer delivery from the absence of an error; that is exactly the bug in the current `EmailSender`.
- Send asynchronously through Hangfire on the existing `tenant` queue, with retry and backoff. Never block an HTTP request on a provider call, and never let a provider failure roll back the business write that triggered it — the same rule the workflow execution recorder already follows.
- Make sends idempotent against retries. A Hangfire retry must not deliver a second copy of the same message to a customer.
- Show the conversation on the lead and contact timelines, integrated with the existing activity timeline rather than as a disconnected inbox.

### Inbound

- Accept inbound replies and delivery receipts by webhook. Verify provider signatures, reject unsigned or replayed callbacks, and resolve the tenant from the routing data — never from a tenant id supplied in the request body, which would let any caller write into any tenant.
- Match an inbound message to its lead or contact where possible, and give unmatched messages a visible, reviewable home instead of dropping them.

### Templates and compliance

- Add tenant-editable message templates with a safe placeholder syntax over lead/contact fields, including custom fields resolved by definition id. Escape every substituted value on render — a lead's name is attacker-controlled input and this output reaches an HTML email.
- Validate templates against real field definitions when saved, so a missing field is caught at edit time rather than silently rendering blank at 3am.
- Honour per-contact channel consent and opt-out, and enforce it at send time in one place rather than at each call site. Record unsubscribe and stop-keyword handling for SMS and WhatsApp. Suppression must be checked for workflow-triggered sends too, not only manual ones.
- Respect quiet hours and rate limits per channel, evaluated in the tenant's timezone.
- Never persist provider secrets, authorization headers, or full message bodies into workflow diagnostics. Reuse `WorkflowDiagnosticRedactor` rather than writing a second redaction implementation.

### Cleanup

- Delete `IronMonkey.ApiService/Common/Services/EmailService.cs` and `IronMonkey.ApiService/Common/Email/EmailSender.cs` and their registrations. Confirm nothing depends on them before removing. Do not leave two email paths in the codebase.
- Replace the workflow engine's logged-only email action with a real dispatch through this layer, keeping its existing failure semantics: a failed send is recorded as a failed action and does not roll back the lead write or stop other rules.

## Acceptance criteria

- A user can send an email and an SMS from a lead, and both appear on that lead's timeline with an accurate status that reflects the provider's actual result.
- A workflow rule with an email action delivers a real message and records success or a categorized failure in the existing workflow execution history.
- Provider failure, timeout, and non-2xx responses are distinguishable, retried sensibly, and never fabricate a "sent" status.
- A Hangfire retry does not send a customer a duplicate message.
- An opted-out contact is not messaged on that channel, including by a workflow-triggered send.
- Inbound webhooks with an invalid signature are rejected; a valid one lands against the right tenant and, where resolvable, the right lead.
- Tenant A cannot read, send as, or receive messages belonging to tenant B; stored provider credentials are never returned by any endpoint.
- The two dead email classes are gone, and tests run with no live provider credentials and no network access.

## Likely implementation surfaces

- `IronMonkey.ApiService/Common/Services/EmailService.cs` and `IronMonkey.ApiService/Common/Email/` (removal)
- New communications feature folder in `IronMonkey.ApiService/Features`
- `IronMonkey.Data/Entities/Notification.cs`, plus new message/template/consent entities and a tenant migration
- `IronMonkey.ApiService/Features/Leads/Workflow/Rules/WorkflowRuleEngine.cs`
- `WorkflowDiagnosticRedactor`
- Lead and contact detail pages, and the activity timeline
- Tests in `IronMonkey.Tests`

Do not build a marketing campaign tool. This is one-to-one conversation history attached to CRM records.
