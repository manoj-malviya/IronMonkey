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
