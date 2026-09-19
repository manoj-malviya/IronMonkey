# Integration Platform, Public API and Enterprise SSO

## Context

The system has one integration path in and one out. In: `ApiKey` — a BCrypt-hashed key with an 8-character prefix, used by the REST ingestion endpoint to create leads. Out: the workflow engine's webhook action, which posts to a URL when a rule fires. There is no published API surface beyond ingestion, no documentation, no way for a tenant to subscribe to record changes, and no way for a tenant's identity provider to authenticate its users.

Each of those blocks a different kind of buyer. A dealership wants leads pushed from its manufacturer's portal and pulled into its DMS. A university needs its student information system to stay in sync. Any organization above a certain size requires SSO before it will approve a purchase — and `PROJECT.md` lists API documentation as a Future requirement while marking an integration marketplace Out of Scope.

`ApiKey` also has gaps that matter as soon as it carries real traffic: no scopes, no expiry, no last-used timestamp, no rotation, and no rate limiting.

## Prompt

Turn the ingestion endpoint into a real tenant-facing platform: a documented API, outbound event subscriptions, and enterprise authentication.

### Public API

- Expose full CRUD over leads, contacts, opportunities, tasks and custom objects, consistent with what the UI can do. An API that only creates leads forces every integrator into screen-scraping or direct database access.
- Version the API from the start and state the compatibility guarantee. Integrations outlive releases, and the first breaking change without a version is the one that loses a customer.
- Publish OpenAPI and generate documentation from the running service rather than maintaining it by hand — hand-written API docs drift within one release.
- Return custom fields keyed by their stable `FieldKey`, not by definition id or label. `FieldKey` exists for exactly this purpose: it is the integration handle, unique per scope, and survives a rename. Values remain *stored* by definition id; this is a mapping at the boundary.
- Apply the same validation, workflow triggers, activity logging and duplicate detection as the UI paths. An API write that bypasses the workflow dispatcher creates records that silently skip every automation the tenant configured.
- Support idempotency keys on writes so a retrying integrator does not create duplicates, and cursor-based pagination on reads so a large export does not drift or skip rows between pages.

### API keys

- Add scopes to `ApiKey` so a key can be read-only or limited to specific record types, plus expiry, last-used timestamp, and rotation with an overlap window that lets an integrator move without downtime.
- Rate-limit per key with clear headers and a `429`, and make limits configurable per plan.
- Show the plaintext key exactly once at creation — the existing `UserPasswordDisplay` pattern — and never again from any endpoint. Continue storing only the hash and the prefix.
- Log key usage enough to answer "what did this key do", and let an Admin revoke a key immediately.

### Outbound events and webhooks

- Let a tenant subscribe to record events — created, updated, stage changed, deleted — per entity type, delivered as webhooks independent of the workflow engine. Workflow webhooks are for automation the tenant authors; event subscriptions are for systems that need to stay in sync.
- Sign every payload so the receiver can verify origin, and include a timestamp and event id so replays are detectable.
- Deliver through Hangfire with retry and exponential backoff, at-least-once, and document that guarantee. Include a monotonic sequence or event id so a receiver can order and deduplicate.
- Disable an endpoint that fails persistently, tell the tenant, and let it replay missed events from a bounded window rather than silently losing them.
- Give subscriptions the same delivery history the workflow feature already has, and redact through `WorkflowDiagnosticRedactor` rather than writing a second redactor — a URL with a signed token in its query string must not be stored in readable history.
- Validate subscription URLs against SSRF: reject loopback, link-local, and private address ranges, resolve DNS at request time and re-check the resolved address, and do not follow redirects to a blocked target. A tenant-supplied webhook URL is a request the *server* makes from inside the network — this is the classic path to the cloud metadata endpoint.

### Enterprise SSO

- Support SAML 2.0 and/or OIDC per tenant, so a tenant authenticates its users against its own identity provider, with optional just-in-time provisioning and group-to-role mapping.
- Keep the existing local login working alongside it, including for platform users. `POST /auth/login` resolves platform users before the tenant lookup, and that path must not become dependent on a tenant's IdP.
- Resolve the tenant from the request without trusting a tenant identifier supplied by the browser. `UserTenantIndex` already gives O(1) email-to-tenant resolution; SSO must not open a second, weaker path.
- Continue to put the user's primary key in the identity claim, never `User.IdentityId` — nothing populates it, it is `""` for every tenant user, and keying authorization on it matches every user in the tenant and unions their permissions. JIT provisioning must populate the same fields local users have, including the `UserTenantIndex` row, without which a user silently cannot log in.
- Enforce that SSO-provisioned users cannot acquire `admin:access` or `SuperAdmin` through group mapping. An IdP group name is tenant-controlled input; mapping it to a platform privilege would let a tenant grant itself the platform.

## Acceptance criteria

- An integrator can authenticate with a scoped key and perform full CRUD against documented, versioned endpoints, with custom fields addressed by `FieldKey`.
- API writes fire the same workflows, activity logs and duplicate detection as UI writes.
- A retried write with the same idempotency key creates one record; paginated reads over a changing dataset neither skip nor duplicate rows.
- Keys can be scoped, rotated with overlap, expired and revoked; plaintext is shown once and is unrecoverable afterwards; limits produce a `429`.
- A tenant receives signed webhooks for record events, can verify them, sees delivery history, and can replay a bounded window after an outage.
- A webhook URL pointing at a private or metadata address is rejected, including via DNS rebinding and redirect.
- A tenant configures SSO and its users log in through their IdP while local login continues to work for platform users; JIT-provisioned users get a `UserTenantIndex` row and can log in.
- No IdP group mapping can grant `admin:access` or `SuperAdmin`.
- Tests cover scope enforcement, idempotency, pagination stability, signature verification, SSRF rejection, SSO assertion validation including replay and audience checks, and privilege-escalation attempts through group mapping.

## Likely implementation surfaces

- `IronMonkey.Data/Entities/ApiKey.cs` and a central migration
- `IronMonkey.ApiService/Features/Leads/Ingestion/Api/`
- New public API feature folders, OpenAPI configuration, and versioning
- New event-subscription and delivery entities plus a tenant migration
- `WorkflowDiagnosticRedactor` and the Hangfire `tenant` queue
- `LoginEndpoint`, `UserTenantIndex`, `AuthorizationService`, `IUserContext`
- Tests in `IronMonkey.Tests`

The tenant-supplied URL and the tenant-supplied IdP group are the two pieces of attacker-influenced input here. Treat both accordingly.
