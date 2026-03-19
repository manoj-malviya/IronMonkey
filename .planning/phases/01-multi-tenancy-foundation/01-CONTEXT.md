# Phase 1: Multi-Tenancy Foundation - Context

**Gathered:** 2026-03-19
**Status:** Ready for planning

<domain>
## Phase Boundary

Tenant signup provisions a fully isolated PostgreSQL database, and every subsequent request automatically routes to the correct tenant data via JWT claims. Includes platform admin dashboard for tenant management (approve/reject/provision). Background jobs carry tenant context explicitly.

</domain>

<decisions>
## Implementation Decisions

### Tenant Signup Flow
- Admin-approved model — no self-service signup
- Signup request collects detailed info: company name, admin email, password, phone, industry type, company size, address, billing contact
- Platform admin sees pending requests, clicks approve or reject with optional note
- After approval, admin manually clicks "provision" button to trigger DB creation
- No automated provisioning on approval — explicit manual step

### Tenant Resolution
- JWT claims only — TenantId embedded in JWT token at login
- Email-based tenant lookup at login: user enters email, system identifies tenant from central DB, then proceeds to password
- One user belongs to exactly one tenant — no multi-tenant user accounts
- Platform admin is a separate role/dashboard, not a tenant user

### Platform Admin
- Separate super-admin dashboard for managing tenants, approvals, system health
- Distinct from tenant-facing application — different UI/routes
- Platform admins are not tenant users; they operate on the central DB

### DB Provisioning
- PostgreSQL for all databases (central + tenant)
- Separate central database holds tenant registry, connection strings, platform admin data, signup requests
- EF Core migrations applied to each tenant DB on provisioning and app updates
- New tenant DB seeded with: admin user account, default statuses, a basic pipeline, default roles
- Connection strings stored in central DB tenant registry

### Background Jobs
- Hangfire with PostgreSQL persistence for job scheduling and execution
- TenantId passed as explicit parameter to every job — no ambient tenant state
- Job execution resolves tenant DB connection from central registry using the TenantId parameter

### Claude's Discretion
- Outbox pattern tenant-awareness: whether to make per-tenant outbox processing in Phase 1 or defer to Phase 2
- Exact Hangfire configuration and dashboard setup
- Migration orchestration strategy for multi-tenant schema updates
- Central DB schema design details

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches for the multi-tenancy infrastructure.

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `Tenant` entity with `Create()` factory method: extend with approval status, connection string, provisioning state
- `User` entity with `TenantId` property: already tenant-scoped
- JWT auth pipeline (`JwtOptions`, bearer authentication): extend to include TenantId claim
- `IUserContext` interface: extend to include TenantId resolution
- `AppDbContext` with `SaveChangesAsync` override and outbox pattern: refactor to become tenant-scoped DbContext
- Fluent Validation pipeline: reuse for signup request validation
- Endpoint pattern (`IEndpoint`, `Map()`, `Handle()`): use for new tenant management endpoints

### Established Patterns
- Static factory methods on entities (`User.Create()`, `Tenant.Create()`, `Role.Create()`)
- Domain events via outbox pattern — events collected in SaveChanges, persisted as OutboxMessages
- Request/Response records nested in endpoint classes
- FluentValidator nested in endpoint class as `RequestValidator`
- Minimal API endpoint registration via `MapEndpoints()` extension

### Integration Points
- `IronMonkey.AppHost/Program.cs`: Add PostgreSQL resources via Aspire
- `IronMonkey.Data/AppDbContext.cs`: Refactor for tenant-aware DbContext factory
- `IronMonkey.ApiService/Program.cs`: Add tenant resolution middleware
- `IronMonkey.ApiService/Authentication/Endpoints/`: Add tenant management endpoints
- `IronMonkey.Common/Auth/`: Extend JWT utilities for TenantId claim

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 01-multi-tenancy-foundation*
*Context gathered: 2026-03-19*
