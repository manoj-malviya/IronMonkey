# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

IronMonkey is a multi-tenant CRM system built with .NET 10.0, .NET Aspire, and PostgreSQL. It uses database-per-tenant isolation with a central database for tenant metadata. Currently in active development — Phase 1 (multi-tenancy foundation) is complete, Phase 2 (configurable lead model) is in progress.

## Build & Run Commands

```bash
# Build the entire solution
dotnet build IronMonkey.sln

# Run via Aspire orchestration (starts PostgreSQL, API service)
dotnet run --project IronMonkey.AppHost

# Run API service directly
dotnet run --project IronMonkey.ApiService

# Run all tests (requires Docker for Testcontainers)
dotnet test IronMonkey.Tests

# Run a single test by name
dotnet test IronMonkey.Tests --filter "FullyQualifiedName~TenantProvisioningTests.Approved_tenant_can_be_provisioned"

# Run tests by class
dotnet test IronMonkey.Tests --filter "FullyQualifiedName~TenantProvisioningTests"

# EF Core migrations (central database)
dotnet ef migrations add <Name> --project IronMonkey.Data --startup-project IronMonkey.ApiService --context CentralDbContext --output-dir Migrations/Central

# EF Core migrations (tenant database)
dotnet ef migrations add <Name> --project IronMonkey.Data --startup-project IronMonkey.ApiService --context TenantDbContext --output-dir Migrations/Tenant
```

## Architecture

### Multi-Tenant Data Isolation
- **CentralDbContext** — Stores `Tenant`, `SignupRequest`, `UserTenantIndex`, outbox messages. Single shared database.
- **TenantDbContext** — Per-tenant isolated database with `User`, `Lead`, `Contact`, `Opportunity`, `Role`, `Permission`. Created during tenant provisioning.
- **TenantDbContextFactory** — Creates TenantDbContext instances for a specific tenant's connection string.
- Global query filters on TenantDbContext enforce `TenantId` and `!IsDeleted` automatically.

### API Layer (IronMonkey.ApiService)
- **Minimal API** endpoints implement `IEndpoint` interface with static `Map()` method.
- Endpoints are registered in `Endpoints.cs` via `MapEndpoint<T>()` extension.
- Services are registered in `ConfigureServices.cs` via `builder.AddServices()`.
- Request/Response DTOs are nested records inside endpoint classes.
- Validators are nested classes (e.g., `CreateUser.RequestValidator`) using FluentValidation (globally imported).
- Typed results pattern: `Results<Ok<Response>, ValidationError, NotFound>`.

### Authentication Flow
1. `LoginEndpoint` checks `PlatformUsers` (central DB) first — see Platform Admin below.
2. Otherwise looks up email in `UserTenantIndex` (central DB) for O(1) tenant resolution.
3. Loads user from tenant DB, verifies BCrypt password.
4. JWT token includes `tenant_id` claim. `IUserContext` extracts current user/tenant from claims.

### Platform Admin (SuperAdmin)
- **Two kinds of caller.** A `PlatformUser` (central DB, `PlatformUsers` table) belongs to no
  tenant and administers the platform. A `User` (tenant DB) is a member of one tenant. They are
  separate entities in separate databases; a platform admin has no tenant DB row.
- **Roles.** Tenants get `Admin` (role 201), created from signup credentials during provisioning.
  `SuperAdmin` (role 1) is the platform operator only.
- **Permissions.** Tenant users resolve permissions from their tenant's `role_permissions` rows.
  Platform users have no such rows, so their grants come from
  `PermissionConstants.ForPlatformRole`. The `tenant_id` claim selects the path
  (`Guid.Empty` = platform). Both live in `AuthorizationService`.
- **Seeding.** `PlatformAdminSeeder` runs at startup from the `PlatformAdmin` config section and
  is idempotent — it never overwrites an existing row, so a rotated password survives restarts.
  Seeding is **skipped unless `PlatformAdmin:Password` is set**, so no working credential is
  committed. The Makefile sets it for local dev, so `make up` seeds the SuperAdmin and
  `make urls` prints the credentials; override with
  `PLATFORM_ADMIN_PASSWORD='...' make up`. `make login` verifies the credential and prints
  a JWT. Because seeding never overwrites an existing row, changing the password has no
  effect until that row is removed — `make reset-admin` does that, then `make restart`.
  Without the Makefile:
  ```bash
  PlatformAdmin__Password='<choose-a-password>' dotnet run --project IronMonkey.AppHost
  ```
  AppHost forwards `PlatformAdmin__Email`/`PlatformAdmin__Password` to the API explicitly —
  `AddProject` gives the child a curated environment rather than inheriting AppHost's.
- **Identity claim is the user's primary key, not `User.IdentityId`.** Nothing populates
  `IdentityId` during provisioning, so it is `""` for every tenant user. Authorization must
  never key on it — doing so matches every user in the tenant and unions their permissions
  (privilege escalation). `LoginEndpoint` puts `User.Id` / `PlatformUser.Id` in the claim.
- **Admin endpoints** are under `/admin/*` and require the `admin:access` permission
  (applied to the whole group in `Endpoints.cs`), e.g. `POST /admin/signup/{id}/approve`,
  `POST /admin/tenants/{id}/provision`, `GET /admin/stats`. A tenant `Admin` is deliberately
  denied `admin:access`.
- **There is no separate SuperAdmin login page.** `POST /auth/login` resolves platform users
  before the tenant lookup, so `/login` handles both. In the web app, platform-only pages
  carry `[Authorize(Policy = AuthorizationPolicies.PlatformAdmin)]` (a `SuperAdmin` role
  check mirroring the API's `admin:access`), and the sidebar's Platform group is wrapped in
  a matching `AuthorizeView`. Platform pages: `/admin/tenants`, `/admin/migrations`.
- **SuperAdmin reaches tenant data by impersonation.** A platform user has no row in any
  tenant DB and their token carries `tenant_id = Guid.Empty`, which `TenantService`
  rejects — so tenant-scoped endpoints are unreachable for them by construction.
  `POST /admin/tenants/{id}/impersonate` mints a **30-minute** token assuming that tenant's
  `Admin` user, so every existing tenant endpoint works unchanged and permissions resolve
  through the normal `role_permissions` path (no special case in `AuthorizationService`).
  The token carries an `act_as` claim naming the platform user, and each issuance writes a
  `TenantImpersonations` audit row. An impersonation token **loses** `admin:access` (so it
  cannot reach `/admin/*`) and cannot mint another. In the web app, `MainLayout` shows a
  persistent amber banner while impersonating; exiting restores the stashed platform token
  client-side (the token is stateless — there is nothing to revoke server-side).
- **`GetUserId()` must not read only `sub`.** JwtBearer leaves `MapInboundClaims` at its
  default of `true`, which rewrites `sub` to `ClaimTypes.NameIdentifier` before the principal
  reaches a handler — so a `sub`-only lookup finds nothing on an authenticated request and
  throws, surfacing as a 500. It falls back to `NameIdentifier`.
- **`User.Roles` is not queryable.** It is a computed property (`=> _roles.ToList()`), so
  `db.Users.Where(u => u.Roles.Any(...))` cannot be translated and throws at runtime. Query
  from `Roles` and traverse `Role.Users`, the real mapped navigation.
- **Tenant roles come from `GET /users/roles`**, not the legacy `GET /roles` — the latter
  queries the obsolete `AppDbContext` against the central DB (where the tenant-scoped
  `Roles` table does not exist) and returned 500. It and `ListRolePermissions` are no longer
  registered in `Endpoints.cs`.
- **Admin pages must call `AdminApiClient`, never `IHttpClientFactory` directly.**
  `BearerTokenHandler` runs in the HttpClientFactory's own DI scope and cannot see the
  circuit's auth provider, so it attaches no token and every call 401s silently. Fetch after
  the first interactive render too — the JWT is in `ProtectedSessionStorage`, unreadable
  during prerender.

### Industry Recipes (`/admin/recipes`)
- **Recipes are platform catalog data, not tenant data.** `IndustryRecipe` is a
  `CentralDbContext` entity — a JSONB snapshot of stages, fields, rules, roles and sample
  leads for a vertical (Blank, Automobile, Education). Provisioning **copies** the chosen
  recipe's content into the new tenant's database as ordinary mutable rows, so a tenant
  freely edits what it was seeded with but never edits the template itself.
- **The write endpoints are gated on `admin:access`, not bare `.RequireAuthorization()`.**
  With only the latter, any authenticated tenant user could `POST`/`PUT`/`DELETE`
  `/api/recipes` and rewrite the catalog every *other* tenant's signup provisions from.
  The `GET`s stay `AllowAnonymous` on purpose — the signup form needs the catalog before
  anyone is authenticated.
- **The four `/admin/recipes` pages and the sidebar link carry
  `AuthorizationPolicies.PlatformAdmin`**, like `/admin/tenants` and `/admin/migrations`;
  the nav link lives inside the existing Platform `AuthorizeView` rather than in a group of
  its own. A bare `[Authorize]` showed the whole recipe surface to every tenant user.
- **That gate is only as strong as the seed data.** It works because no tenant role is
  granted `admin:access` — only `Role.SuperAdmin` (role 1, platform-only) is, in
  `RolePermissionConfiguration`. `RecipeAdminPermissionTests` pins that invariant, since a
  future seed change could reopen the hole with nothing else failing.

### Configuration Workspace (`/admin/configuration`)
- **Tabs are query-string addressed** (`?tab=stages|fields|routing|workflows`) via
  `[SupplyParameterFromQuery]`, so refresh and deep links keep the area. Each tab is its own
  component (`PipelineStagesTab`, `CustomFieldsTab`, `RoutingTab`); the page is a shell that
  owns tab state and the shared result banner. Workflow rules stayed on the page unchanged.
- **Stage ordering is not unique in the database.** A reorder rewrites the whole sequence and
  passes through positions other stages still hold, which a unique index would reject
  mid-statement. `PUT /api/pipeline-stages/order` takes the complete id list, writes it in one
  transaction, and **409s if the submitted set differs from the stored set** — that is the
  concurrency check. Stage *names* are unique per tenant, case-insensitively, via a raw-SQL
  `lower(Name)` index (EF cannot express an expression index).
- **Nothing referenced is deleted silently.** `GET /api/pipeline-stages/{id}/impact` and
  `GET /api/custom-fields/{id}/impact` report the blast radius, and the destructive endpoints
  enforce it: a stage holding leads needs `?reassignTo=`, the last active stage is always
  refused, and a field holding values can only be archived (`?archive=true`), never deleted.
  `IConfigurationUsageService` is the single source of those counts.
- **A lossy custom-field edit requires an explicit strategy.** Changing type or scope, or
  removing an in-use option, returns **409 with a `MigrationRequired` body** unless the request
  carries `MigrationStrategy` of `Keep` or `Clear`. The UI renders that as a radio choice.
- **Custom fields gained `FieldKey`, `HelpText`, `DefaultValue`, `IsArchived`.** `FieldKey` is a
  stable integration handle, unique per scope, backfilled from the label by the
  `ConfigurationWorkspace` migration — **values are still keyed by the definition Id**, never
  the key or the name. Archived fields are hidden from forms by default; `ListCustomFields`
  takes `?includeArchived=true` for the workspace.
- **`CustomFieldValueBinder` handles archived fields and defaults**, so every call site gets it
  right. An archived field is never *required* (archiving one would otherwise lock every record
  in the tenant) but keeps any value already stored; a blank submission falls back to
  `DefaultValue` through the same validation as a typed value.
- **A non-nullable `bool` query parameter is *required* in minimal APIs.** `includeArchived`
  and `archive` are `bool?` for exactly this reason — as `bool` they 500'd every caller that
  omitted them, including the lead and contact forms.
- **Raw SQL must filter `TenantId` explicitly.** The usage counter and the "clear values"
  update both touch the `custom_field_values` jsonb directly, which bypasses the global query
  filters — without the predicate they read and write across every tenant in the database.
  Use `jsonb_exists(col, @key)`, not `?`, which Npgsql parses as a parameter placeholder.
- **Territory routing is structured.** `TerritoryRuleSet` parses both the new
  `{"rules":[{name,values,assigneeId}]}` shape and the original flat `{"value":"guid"}` map,
  and flattens to the lookup `LeadRoutingService` already used. `ConfigureRoutingEndpoint`
  validates against real tenant users before storing, so invalid JSON is never persisted. The
  UI edits rules as rows; the JSON view is an escape hatch that must be applied before saving.
  (The old page posted `Dimension = "Agent"`, which is not a valid enum value — routing saves
  had been failing.)

### Workflow Execution History (`/admin/workflow-runs`)
- **Tenant-visible run history lives in the tenant database**, not in `ILogger` or Hangfire.
  `WorkflowExecutionLog` + `WorkflowExecutionStep` are `TenantDbContext` entities, so they
  inherit database-per-tenant isolation and the global `TenantId` filter. Structured logs are
  still written alongside for operators — the two layers answer different questions.
- **`Skipped` is a success, not a failure.** A condition that did not match is an intentional
  non-event; reporting it as an error would make every narrow rule look broken.
  `CompleteFromSteps` derives the run status from the **action** steps only — counting the
  condition step (always `Succeeded` on that path) made a single failed action read as
  `PartiallySucceeded`.
- **The uniqueness key is `(TenantId, CorrelationId, WorkflowRuleId, Attempt)`.** One trigger
  evaluates every matching rule and each gets its own row, so without `WorkflowRuleId` the
  second rule's insert collides and only the first rule's history is ever written.
  `CorrelationId` identifies the *trigger*; a Hangfire retry reuses it with a higher `Attempt`,
  which is what makes a retry readable as a retry rather than as an unrelated run.
- **Everything persisted passes through `WorkflowDiagnosticRedactor`.** Exception text routinely
  quotes the input that caused it — a webhook URL with a signed token in the query string, an
  `X-Api-Key` header value — and this table is readable and exportable by a tenant Admin. URLs
  keep scheme/host/path and lose the query; emails are masked to `j***@example.com` (domain kept
  because a misrouted rule is the failure worth seeing); webhook response bodies and rendered
  email bodies are never stored at all.
- **The row is written before the action runs.** A worker killed mid-action leaves a `Running`
  row as evidence the trigger was received; `WorkflowExecutionMaintenanceJob` (hourly) marks
  rows stale past the threshold as `Abandoned` — not `Failed`, because whether the action ran is
  genuinely unknown — and applies the retention window. A row it *just* reconciled is held back
  from deletion for one cycle, or the `Abandoned` state it was given would never be observable.
- **Recording never changes what the workflow does.** Every write in `WorkflowExecutionRun`
  swallows its own exceptions after logging: an audit write that throws would roll back the
  business effect the audit exists to describe. `IWorkflowExecutionRecorder` is an optional
  constructor dependency on the engine, so tests can construct it directly.
- **`workflow:logs:read` gates the endpoints**, granted to `Admin` and `SuperAdmin` by seed.
  It is separate from `settings:read` so the grant can be withheld from a role that may
  configure automation but should not see the leads it ran against. A platform SuperAdmin still
  reaches tenant history only through impersonation — `tenant_id = Guid.Empty` is rejected by
  `TenantService`, as on every tenant endpoint.
- **`PerformContext` is a job parameter, not an injected service.** `WorkflowRuleEvaluationJob`
  takes it so the run can record Hangfire's job id and retry count; the dispatcher passes `null`
  at enqueue time and Hangfire substitutes the live context at invocation.

### Communications (`/api/messages`, `/admin/messages/unmatched`)
- **Providers are platform-wide, not per-tenant.** Credentials come from the `Communications`
  configuration section and secrets, never from tenant-editable data. Per-tenant credentials
  would put an API key in a table every tenant Admin can read, and any endpoint returning
  tenant settings would become a credential leak. Tenants get a from-identity, not a secret.
  No endpoint returns provider configuration — `GET /api/messages/channels` reports
  availability only.
- **Absent credentials disable a channel cleanly**, like the `PlatformAdmin` seeder. An
  unconfigured provider reports `IsConfigured == false` and `MessageProviderRegistry` indexes
  only configured ones, so it is indistinguishable from an absent one at the call site. Sends
  are then recorded as `Rejected`/`ChannelNotConfigured` — an actionable diagnostic — rather
  than failing at the network layer with something that reads like an outage. Nothing throws
  at startup. `Communications:UseNoopProviders` forces the no-op provider for every channel,
  which is how tests run with no credentials and no network call.
- **`IMessageDispatcher` is the only way to send.** Consent, channel rules and provider
  resolution all live there, so a manual send and a workflow-triggered send cannot diverge —
  an opt-out honoured in one call site but not another is not an opt-out. Suppression is
  checked at queue time *and* again at delivery, because an opt-out can arrive while a message
  sits through a retry backoff.
- **`Sent` is not `Delivered`.** A provider accepting a message is a handover, not delivery;
  only a verified receipt writes `Delivered`. `Failed` is retryable and `Rejected` is not, so
  a suppressed recipient or a rejected credential does not burn three retries.
  `Message.CanAttemptSend()` is the duplicate-send guard: a Hangfire retry of a job whose
  provider call already succeeded stops there instead of sending a second copy.
- **An idempotency key must not read from the audit object.** The workflow email action derives
  its key from the rule id, lead id and trigger — never from `WorkflowExecutionRun`, which is
  optional and whose row can be rejected by the unique correlation index. A `run?.X ?? fallback`
  term silently changed the key on a retry and delivered the customer a second copy.
  `(TenantId, IdempotencyKey)` is uniquely indexed, so the guarantee is the database's.
- **Quiet hours defer; the rate limit rejects.** `MessagingPolicy` (tenant DB, one row per
  tenant, absent = unrestricted) holds both. A message inside the quiet window stays `Queued`
  and is Hangfire-scheduled for when the window closes — the tenant asked for it to go, just
  not at 3am, so dropping it would lose a message. A rate-limited one is `Rejected`, because
  there is no known time at which it becomes acceptable. The window is evaluated in the
  tenant's timezone from `TenantFormatting`, the same one the UI renders dates with; an
  overnight window has `Start > End` and a naive `start <= t < end` comparison reports it as
  never active.
- **Consent is keyed by normalized address, not by record id.** The same person is routinely
  several leads and a contact, so keying on a record would let the next duplicate be messaged
  after a STOP. Every read and write goes through `ConsentAddress.Normalize` — asymmetry there
  means the lookup misses and someone who opted out gets messaged.
- **Webhooks take the tenant from a routing token in the URL path**, derived by HMAC from the
  tenant id, never from the request body — an anonymous endpoint's body is attacker-supplied,
  and a tenant id in it would let anyone write into any tenant. The provider signature is the
  authentication (fixed-time compared, with a replay window on the timestamp); the token only
  routes. An unsigned or mis-signed callback is refused before anything is read from it.
- **Every substituted template value is escaped.** A lead name arrives from a public web form,
  so it is attacker-controlled, and it reaches both an HTML email and the tenant's own browser.
  `MessageTemplateRenderer` escapes by output format (HTML for email, verbatim for SMS), and
  templates are validated against real field definitions at save time so a bad placeholder is
  caught by the author rather than rendering blank at 3am.
- **`EmailService` and `EmailSender` are gone.** They were dead code from another product —
  hardcoded sender, commented-out TLS, a fire-and-forget send logged as success via
  `true ? ... : ...`. There is exactly one email path now.

### Tenant CRM Dashboard (`/admin`)
- **One endpoint per widget, not one payload.** `/api/dashboard/{summary,opportunities,attention,activity}`
  are separate so a widget that fails degrades alone — the page renders the rest, shows that
  panel's own error with its own retry, and reports "N of 4 panels could not be loaded".
  `WidgetState<T>` holds each panel's state; a panel that fails *after* a successful load
  keeps its figures and marks them **stale** rather than blanking good numbers.
- **Date ranges are half-open — `[From, ToExclusive)`.** `DashboardDateRange` converts an
  inclusive end *date* to the next midnight. A closed `<= end-of-day` bound drops rows in the
  final tick, because `23:59:59.9999999` is not the end of the day and Postgres `timestamptz`
  keeps microsecond precision a .NET tick bound rounds against. Presets, an inverted custom
  range (swapped, not empty), and explicit dates without a preset (treated as custom) are all
  resolved in that one type, and unit-tested without a database.
- **`/api/dashboard/attention` is deliberately NOT date-filtered.** An overdue task is overdue
  whatever window is being inspected; hiding it because it was created outside the range would
  make the panel misleading. "Open leads" likewise excludes leads in a terminal stage, not just
  converted ones — a lead in Closed Lost is finished work.
- **Conversion time is approximated by `Lead.UpdatedAt`.** `Convert()` mutates the row, so the
  save that records the conversion stamps `UpdatedAt`. A later edit moves it, which is fine for
  a "recent" feed and avoids a `ConvertedAt` column + migration for a display-only field.
- **Never format money with `"C0"`.** The app runs under the invariant culture, whose currency
  symbol is the placeholder `¤` — `"C0"` renders `¤308,000`. Forcing a culture would assert a
  currency the tenant may not use. Use `MoneyFormat` (matches the existing `"N0"` convention).
- **Handlers are `internal`, not `private`.** `InternalsVisibleTo("IronMonkey.Tests")` lets the
  tests invoke the real handler, so the aggregation under test is the aggregation that ships —
  unlike the older dashboard tests, which re-implement the LINQ they claim to cover.

### Search, Saved Views, Report Builder, Exports (`/admin/search`, `/admin/views/{type}`, `/admin/reports/builder`, `/admin/exports`)
- **One query engine: `Features/Insights/QueryCompiler`** compiles view filters, sorts and report
  definitions to **parameterised SQL**. Field SQL comes only from `RecordCatalog` (built-ins are
  constants; custom fields are `custom_field_values->'Values'->>@p` with the definition id bound as a
  parameter). Unknown fields/operators are rejected before SQL exists; every value is an
  `NpgsqlParameter`. Filters cap depth (3), conditions (30), list sizes (100), strings (500).
- **Raw SQL bypasses the EF query filters**, so the compiler and `SearchService` restate them:
  explicit `TenantId` + `IsDeleted` and the caller's owner sets from `db.Visibility` (tasks also check
  the parent lead). **Any new raw-SQL insight path must do the same** — the EF filter will not save it.
- **Every insight statement runs in a READ ONLY transaction with `SET LOCAL statement_timeout`**
  (`InsightSql`): 3 s search, 10 s interactive, 60 s export/scheduled. A timeout is PostgreSQL
  cancelling server-side (57014 → `QueryTimeoutException` → 504), not a client abandoning it.
- **Time boundaries go through `DashboardDateRange.ResolveIn(preset, from, to, zone, now)`** — the
  same preset logic run on the tenant's wall clock, converted to UTC instants, still half-open.
  Time buckets use `date_trunc(@gran, col AT TIME ZONE @tz)`. Custom Date fields compare as
  tenant-local dates. The four fixed reports were left on their own endpoints, unchanged.
- **Search** is PostgreSQL trigram (`pg_trgm`, GIN indexes from the `SearchViewsReports` migration),
  not an external engine. Phones match via the stored generated `MobileDigits` column and
  `SearchNormalizer.PhoneTail` (strip non-digits, drop trunk zeros, keep the last 10) — so
  "07700 900123" finds "+447700900123". Custom fields are searched only when `IsSearchable`
  (indexed jsonb-text prefilter + exact-key EXISTS). Per-type caps, counts capped at 100, and only
  types the caller holds `*:read` for are searched at all.
- **Saved views store a question, not an answer**: a shared view runs under each viewer's own
  visibility. Only the owner edits/deletes. Stored definitions are re-validated on every read, so a
  view on a deleted custom field reports itself broken instead of returning an empty list.
  `/admin/views/{type}` keeps state in the URL (`view`, `f` = base64url definition, `page`), so
  Back/Forward works; `returnUrl` goes through `ListReturn.Resolve`. Every sort ends `, t."Id"`.
- **Reports** cap groups at 200 (fetch 201 to flag truncation). Scheduling uses a Hangfire recurring
  job per report (`report-{tenant}-{id}`, `tenant` queue) that runs **as the report's owner**:
  background jobs have no ambient visibility, so `ActingAs.OpenAsync` resolves and enters the user's
  visibility explicitly — the same applies to exports. Stored runs are owner-only.
- **Exports** need `data:export` (id 23, Admin by default) plus the type's read permission, are built
  by `DataExportJob` on the `tenant` queue (≤ 50,000 rows), re-check permission at run time, and
  download only for the requester until they expire (7 days). The `export_jobs` row is the audit
  record and is never deleted; completion and each download also write `ActivityLog`. CSV cells
  starting `= + - @` are apostrophe-prefixed (formula injection: lead names come from public forms).
- **Impersonation picks an arbitrary Admin** when a tenant has several (pre-existing), so two
  impersonation tokens for one tenant can be different users — exports, views and defaults are
  per-user and will not follow.

### Record Visibility, Teams and Roles (`/admin/roles`, `/admin/teams`)
- **Visibility is enforced in the `TenantDbContext` global query filters**, beside `TenantId`.
  `RecordVisibilityMiddleware` (after `UseAuthorization`) resolves the caller's scope and makes it
  ambient (`RecordVisibility.Enter`, an `AsyncLocal`); every context constructed in that request
  captures it into the filter fields. So lists, `TotalCount`, detail lookups (an invisible id is a
  plain 404), dashboard aggregates, duplicate checks, merges and reports all inherit it with no
  per-endpoint code. Contexts with no ambient visibility — background jobs, provisioning, the
  public quote page — are `Unrestricted`, which is what lets routing assign to anyone.
- **`IgnoreQueryFilters()` on Leads/Contacts/Opportunities now bypasses visibility, not just
  soft-delete.** Duplicate detection's fuzzy-name path did exactly that and reported invisible leads
  as duplicates. Use it only on non-record tables (Users, numbering).
- **Ambient visibility only flows downward.** Setting it inside an awaited helper reverts when the
  helper returns; resolve first, then `using (RecordVisibility.Enter(await ...))` in the caller.
- **Scopes:** `Own` (owner = me), `Team` (me + teammates + everyone in teams I manage and their
  sub-teams), `All`. Per role, per record type (Lead, Contact, Opportunity, Task) in
  `role_record_scopes`; **no row = All**, so existing tenants are unchanged until an Admin narrows
  one. A user's scope is the broadest across their roles. **Unowned records are visible only under
  All.** Owner = `Lead.AssignedToUserId`, `LeadTask.AssignedToUserId`, and the new
  `Contact/Opportunity.OwnerUserId` (backfilled from the converting lead; set to the creator on
  create). A lead created by a narrowed user with no assignee is assigned to them, or they would
  lose sight of it on save. A task is visible only if its own assignee is in scope AND its lead is.
- **Derived records follow their parent's visibility in the filter:** `ActivityLog` (by subject),
  `WorkflowExecutionLog` (by lead), `Message` (by lead/contact), `Quote` (by opportunity).
- **Teams nest via `ParentTeamId`; depth ≤ `TeamHierarchy.MaxDepth` (5) and cycles are rejected at
  write time**; the resolver's walk is also capped and cycle-safe as a backstop.
- **Visibility is cached 5 min per user behind a tenant-wide version stamp**
  (`IRecordVisibilityCache.InvalidateTenantAsync`), bumped on every role-scope, role-assignment and
  team change — Team scope for one user depends on other users' memberships. Changes apply on the
  caller's next API request; the Blazor circuit only holds a token, so it cannot outlive a revocation.
- **Permissions are now enforced on CRM records:** groups built with `RequireRecordPermissions(read,
  write)` gate GET on `*:read` and everything else on `*:write` (delete included — no tenant role
  holds `*:delete`, and requiring it would remove a capability Admins have today). The migration
  granted CRM read/write to every existing role except SuperAdmin, which is exactly the access each
  had in effect before. **Role and team management require `users:write`** — before this, any
  signed-in user could create a role with every permission and assign it to themselves.
- **`PrivilegeGuard`: nobody grants what they don't hold.** Adding a permission to a role, or
  assigning a role, requires the actor to hold every permission involved; a role scope can't be set
  wider than the actor's own. The Admin role must keep `users:read`/`users:write` (lockout guard,
  alongside `AdminSafetyGuard`'s last-Admin rule). `admin:access` and SuperAdmin stay hidden and
  ungrantable via `TenantRoleRules`.
- **`GET /api/users/assignable`** (id + name, any tenant user) is the assignment picker source —
  `GET /users` needs `users:read`, and assignment must reach users whose records you can't see.
- **Privilege changes are audited in `ActivityLog`** (`PermissionAudit`: RoleCreated,
  RolePermissionsChanged, RoleScopesChanged, RoleDeleted, Team*), with before/after values.

### Products, Quotes and Deal Economics (`/admin/products`, `/admin/quotes/{id}`, `/admin/reports/revenue`)
- **Deal value is computed from line items, never stored as a free decimal.** `Opportunity.Amount`
  is still a column (dashboards aggregate it in SQL) but only `RecalculateTotals()` writes it, via
  `LineCalculator`. **Load `LineItems` before calling anything that recalculates** — the sum is
  over the in-memory collection, so a deal loaded without `.Include(o => o.LineItems)` recomputes
  to zero and overwrites its stored value. `DealEconomics.LoadAsync` does this for the endpoints.
  `SetAmount(x)` survives as the lump-sum path: it replaces the lines with one free-text line.
- **The `ProductsAndQuotes` migration gave every existing opportunity exactly one migrated line**,
  including `Amount = 0` (the old schema could not tell zero from unset), flagged `IsMigrated`.
  `PUT /api/opportunities/{id}` still accepts `Amount`, but only for a lump-sum deal (no lines, or
  one free-text one-off line); on a catalog-built deal a differing amount is refused.
- **One rounding rule, in `MoneyMath`/`LineCalculator`:** round to the currency's minor unit
  (0 for JPY, 3 for KWD, else 2), midpoint **away from zero**, applied **per line** (gross →
  discount → tax on the discounted net); deal and quote totals are plain sums of rounded lines and
  are never rounded again, so a printed quote adds up when checked by hand. Display uses
  `TenantFormatting.Money(value, currency)` (UI: `Presentation.MoneyExact`) — the same formatter
  the quote document uses. The UI never multiplies or rounds; it formats API figures.
- **Currency: `CurrencyCode == null` means the tenant's own currency.** That is what every
  pre-existing deal meant, and it keeps them aggregating together. A deal in another currency is
  summed with tenant figures only through the `ExchangeRate` (+ `ExchangeRateDate`) recorded on it;
  with no rate, `MoneyAggregator` **excludes and reports** it (`UnconvertedCount`), never adds it at
  face value. Any SQL `SUM(Amount)` must group by currency and rate and go through
  `MoneyAggregator` — see `GetDashboardOpportunitiesEndpoint`. A price list prices in one currency
  and a line from a list in another currency is refused.
- **Prices are versioned, append-only `ProductPrice` rows** effective over `[From, To)`. A change
  inserts a version and closes the previous one; `UnitPrice` is never rewritten, and back-dating
  before the current version is refused. Precedence is fixed (`PriceResolver`): line's price list →
  deal's price list → tenant default — first one holding an effective price wins, no cheapest-price
  search. Lines copy the unit price and record the `ProductPriceId` they came from.
- **Quotes are snapshots and immutable once sent.** Lines and totals are copied into `quote_lines`
  at draft time (or "refresh"); every mutator refuses a non-draft. Changing a sent quote is
  `revise` → a new version (same number), and **sending** it marks every other open version of that
  number `Superseded` and revokes their links. `(TenantId, Number, Version)` is unique. Expiry is
  enforced lazily whenever a quote is read or responded to — use `TenantCommerceContext.TodayAt(now)`
  with the injected `TimeProvider`, not `Today`, or expiry is untestable.
- **Discount approval = tenant threshold (`QuoteSettings.ApprovalDiscountThresholdPercent`) +
  the `quotes:approve` permission** (seeded to Admin and SuperAdmin; permission id 21). `MarkSent`
  refuses an unapproved quote server-side; refreshing a draft clears its approval. There is no
  manager hierarchy in the model yet, so "who approves" is purely the permission.
- **Catalog, price-list, price-version and quote-settings writes need `catalog:write`** (id 22,
  seeded to Admin and SuperAdmin) — **not `settings:write`, which the tenant Admin role does not
  hold.** Gating on `settings:write` made the whole catalog unwritable for every tenant user; the
  bug only showed up driving the real app.
- **The `/q/{routingToken}/{token}` customer page is anonymous attack surface.** Tenant from the
  HMAC routing token (same derivation as webhooks, `IWebhookTenantResolver`), quote from the
  SHA-256 of a 256-bit token — only the hash is stored, the URL is shown once. Unknown/expired/
  revoked/cross-tenant links all return the same 404 page. The page shows only the quote (no deal
  title, no unit cost), with CSP `default-src 'none'`, `no-referrer` (the token is in the URL),
  `no-store`. Set `Quotes:PublicBaseUrl` in production; it falls back to the request host.
- **Quote events go through the existing rails**: delivery via `IMessageDispatcher` (idempotency
  key `quote:{id}:sent`), and `QuoteSent`/`QuoteAccepted`/`QuoteRejected` workflow triggers dispatched
  on the lead the deal was converted from. A deal created directly has no lead, so no rule runs —
  logged, not invented.
- **Product attributes are custom fields with `CustomFieldEntity.Product`** through the same
  binder. Raw SQL over a scope's values must use `CustomFieldEntityTables.TableName()` — the old
  two-way `Lead ? "leads" : "contacts"` ternaries would have sent product SQL to `contacts`.
- **Recipes may carry `Catalog`** (products, opening list prices, quote template); null on every
  recipe stored before it, which provisions to an empty catalog. The `RecipeStarterCatalogs` central
  migration adds catalogs to Automobile/Education only where the key is absent, so admin edits are
  never overwritten. A recipe custom field's optional `AppliesTo` defaults to Lead.
- **Child entities added through a parent's collection need `ValueGeneratedNever()` on their Guid
  key** (line items, quote lines, status changes). By convention EF treats a discovered child with a
  preset key as an existing row and issues an UPDATE that affects nothing.

### List Pages
- **`GET /api/leads` returns a page object, not an array** — `{Items, TotalCount, Page, PageSize,
  TotalPages}`. The total is counted *before* paging so "1–25 of 240" is honest, and a page past
  the end clamps to the last real page rather than returning an empty list that reads as "no
  results".
- **Every sort ends with a tiebreak on `Id`.** Without it, rows sharing a sort value have no
  defined order between queries, so a row can appear on two pages or on none. Sort keys are an
  allow-list: an arbitrary property name would let a caller order by columns the list never
  exposes, and an untranslatable one throws at runtime.
- **Filter state lives in the URL.** `LeadList` drives `search`/`stageId`/`sort`/`page` through
  `[SupplyParameterFromQuery]`, so Back/Forward moves through filter states and a shared link
  reproduces the view. Detail links carry the list URL as `returnUrl`; `ListReturn.Resolve`
  validates it is a same-origin relative path before navigating — echoing it back unchecked
  would be an open redirect.

### Background Jobs
- Hangfire with PostgreSQL storage, queues: `default`, `tenant`.
- Outbox pattern: domain events saved as `OutboxMessage` in `SaveChangesAsync`, processed by Hangfire.

### Aspire Orchestration (IronMonkey.AppHost)
- PostgreSQL container + `CentralDb` database. The container is declared
  `ContainerLifetime.Persistent` with a named data volume (`ironmonkey-postgres-data`) and a
  pinned password, because **tenant databases are created on this same server at provisioning
  time**. Without that, `make down` removed the container and destroyed every provisioned
  tenant. `make down`/`restart` now leave PostgreSQL running; `make reset-db` wipes it
  deliberately (with a confirmation prompt).
- **Stored tenant connection strings are rebased at read time.** Provisioning snapshots the
  whole central connection string — host, port, password — into `Tenant.DatabaseConnectionString`,
  and Aspire can hand out a different host port on a later run, stranding every tenant.
  `ITenantConnectionStringResolver` keeps the stored *database name* and takes the server
  coordinates from live configuration. Every consumer goes through it: `TenantService`,
  `TenantRegistry`, `AuthorizationService`, `LoginEndpoint`, `ImpersonateTenantEndpoint`.
  It is an optional constructor dependency on the services so tests can construct them
  directly (the test fixture's connection string is already current).
- API service uses Aspire service discovery (`https+http://apiservice`).
- Web frontend (Blazor Server) exists but is currently commented out in AppHost.

## Key Conventions

- **C# 14 extension members**: `extension(WebApplicationBuilder builder) { ... }` syntax used in `ConfigureServices.cs` and `Endpoints.cs`.
- **Entity factory methods**: Use `Entity.Create(...)` static factory pattern, not constructors.
- **File-scoped namespaces**: `namespace IronMonkey.ApiService.Authentication.Endpoints;`
- **Namespace pattern**: `IronMonkey.[Project].[Feature]` (e.g., `IronMonkey.Data.Entities`).
- **Private fields**: `_camelCase` with underscore prefix.
- **No linter/formatter config**: Relies on standard C# conventions and nullable reference type checking.

## Testing

- **Framework**: xUnit 2.9.3 with Moq 4.20.72.
- **Integration tests** use Testcontainers (`postgres:15-alpine`) via shared `PostgreSqlFixture`.
- **Docker required**: Tests spin up real PostgreSQL containers — no mocked databases.
- Tests are in `IronMonkey.Tests/Integration/` and `IronMonkey.Tests/Unit/`.

## Dependencies to Note

- **EF Core 10.0.5** with **Npgsql 10.0.1** (must use matching major versions).
- **Aspire 13.1.0** for orchestration and service defaults.
- **Hangfire** for background job processing.
- **Serilog** for structured logging.
