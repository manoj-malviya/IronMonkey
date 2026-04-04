# Milestones

## v1.3 Public Landing & Tenant Signup (Shipped: 2026-04-03)

**Phases completed:** 2 phases, 6 plans, 10 tasks

**Key accomplishments:**

- PublicLayout.razor and FeatureCard.razor created — clean public shell with sticky nav, dark footer, and reusable feature card component ready for Home.razor
- Responsive public SaaS landing page replacing Blazor placeholder — dark gradient hero with tagline/CTAs plus 4-card feature grid using PublicLayout and FeatureCard components
- Routes.razor updated with public path exclusion in NotAuthorized block; Login.razor adopts @layout PublicLayout — both public pages now accessible to unauthenticated visitors without AdminSidebar
- Two self-contained Blazor recipe selection components — RecipePreviewCard (clickable card with selected-state indigo border) and RecipePreviewPanel (expandable content panel with inline RecipePreviewResponse record) — ready for use in Signup.razor
- Public tenant signup form at /signup with 7 validated fields, recipe browser with toggle-select and preview panel, POST /auth/signup integration with loading/error states, and navigation to /signup/success on 201 success

---

## v1.2 Admin UI (Shipped: 2026-04-02)

**Phases completed:** 5 phases, 16 plans, 20 tasks

**Key accomplishments:**

- Tailwind CSS v4.2.2 standalone CLI integrated into MSBuild pipeline via BeforeTargets hook, Bootstrap removed from App.razor, and IronMonkey.Web registered as webfrontend in Aspire AppHost orchestration
- JWT-backed AuthenticationStateProvider with ProtectedSessionStorage, auto-Bearer DelegatingHandler, and named AdminApi HttpClient registration in Program.cs
- Login page with JWT auth flow, AdminSidebar with 4 Heroicon nav groups, mobile topbar, MainLayout shell, auth-guarded /admin index, and router-level NotAuthorized redirect to /login
- RecipeList.razor
- Blazor collapsible RecipeContentEditor component and RecipeCreate page with auto-slug generation, POST to /api/recipes, and 401 redirect
- One-liner:
- GET /admin/tenants and GET /admin/signup/{id} Minimal API endpoints with TenantSummary/SignupRequestDetail projections and 5 passing integration tests
- Two-tab Blazor page at /admin/tenants combining tenant list and signup requests with expandable row detail, plus sidebar cleanup removing the redundant /admin/signups link
- One-liner:
- One-liner:
- Blazor Server UserList.razor at /admin/users with sortable data table, Show Deactivated toggle, and confirmation modal for soft-deleting users via DELETE /api/user-management/users/{id}
- Blazor UserCreate and UserEdit pages with shared UserPasswordDisplay component for copy-to-clipboard password reveal after create/reset
- One-liner:
- SystemConfiguration.razor at /admin/configuration — 4-tab shell with complete Pipeline Stages CRUD (inline edit, reorder arrows, create, delete modal)
- Custom Fields tab (inline CRUD with type-conditional Options) + Routing tab (mode radio + conditional Territory JSON textarea) replacing placeholder panels in SystemConfiguration.razor
- Commit:

---

## v1.1 Tenant Onboarding with Industry Recipes (Shipped: 2026-03-27)

**Phases completed:** 3 phases, 9 plans, 16 tasks

**Key accomplishments:**

- IndustryRecipe entity with JSONB content model, EF configuration, and tenant recipe tracking fields
- Blank/Custom recipe seed with fixed GUID for deterministic provisioning
- Transactional recipe application during tenant provisioning — stages, custom fields, and workflow rules from JSONB content
- Automobile Dealership and Educational Institution domain recipes with sample leads seeded on provisioning
- Recipe selection integrated into signup-to-provision flow with deactivated-recipe guard and Blank recipe fallback
- 5 recipe API endpoints (list, preview, create, update, deactivate) with RecipeContentValidator
- 14 RecipeEndpointTests covering all Phase 8 requirements — 143+ total integration tests

---

## v1.0 MVP (Shipped: 2026-03-24)

**Phases completed:** 5 phases, 30 plans, 59 tasks

**Key accomplishments:**

- xUnit 2.9.3 test project with TestContainers PostgreSQL fixture and 10 skipped stub tests covering all Phase 1 requirements (TNCY-01, TNCY-02)
- CentralDbContext + TenantDbContext with EF Core global query filters for TNCY-02 isolation, Npgsql 10.0.1, and passing integration tests via TestContainers
- tenant_id JWT claim embedded in login, TenantService resolves per-tenant connection string via central DB index, LoginEndpoint performs 3-step email→tenant→password flow using BCrypt
- Signup-to-provision workflow: BCrypt-hashed POST /auth/signup, admin approve/reject endpoints, and TenantProvisioningService creating isolated PostgreSQL databases via NpgsqlConnection with MigrateAsync() and seeded admin user — validated by 4 integration tests using TestContainers
- One-liner:
- All 10 Phase 1 integration and unit tests passing: EF migration gap for OutboxMessage.Published fixed, phase gate green with zero failures and zero skipped tests.
- CustomFieldTests.cs
- EF Core data layer with LeadSource enum, PipelineStage FK, JSONB custom fields, and Phase2_LeadModel migration across 5 new entity files and 4 EF configuration files
- 6 Minimal API endpoints for custom field definitions, pipeline stages, and lead creation — all using ITenantDbContextFactory for per-tenant DB isolation and RequireAuthorization() for JWT enforcement
- FuzzySharp-powered duplicate detection (email/phone/name) and lead merge with custom field transfer, soft-delete, and JSON snapshot audit trail
- 17 integration tests with real PostgreSQL via TestContainers covering all Phase 2 requirements — custom fields, pipeline stages, lead source tracking, duplicate detection, and lead merge with audit
- Wired CheckDuplicatesEndpoint and MergeLeadsEndpoint into MapLeadsEndpoints() — both endpoints now HTTP-accessible
- Seven [Fact(Skip)] stub files establishing the behavioral test contract for all four lead ingestion channels — manual entry, CSV import, API key, and web form — before any implementation begins.
- ApiKey, WebForm, and ImportBatch entities with EF Core migrations for both central and tenant databases, plus CsvHelper package and Lead duplicate-detection fields
- BCrypt-based API key generation and validation with external lead ingestion endpoint (X-Api-Key auth) returning duplicate detection results
- CSV import channel: upload endpoint, Hangfire background job with per-row error isolation and duplicate flagging, status polling, and error CSV download
- One-liner:
- One-liner:
- 34 xUnit tests (13 unit + 21 integration) proving all four Phase 3 ingestion channels work end-to-end against real PostgreSQL via TestContainers
- 26 [Fact(Skip)] stubs across 6 test files establish Nyquist-compliant RED state for all PIPE-01 through PIPE-05 behaviors, with Stateless 5.20.1 and RulesEngine 6.0.0 added to ApiService and Tests projects
- One-liner:
- State machine validation service against StageTransitions DB table, Kanban board with 20-lead virtual scroll per column, and lead move endpoint with D-08 inline error format
- Task CRUD endpoints (POST/GET/PUT) for lead-linked tasks and round-robin/territory lead routing service with upsert config endpoints
- One-liner:
- 23 Phase 4 integration tests implemented replacing all Fact(Skip) stubs — state machine, Kanban, tasks, routing, and workflow rules verified against real PostgreSQL
- 24 skipped xUnit stubs across 5 integration test files establishing behavioral contracts for ACTV-01, REPT-01, REPT-02, and REPT-03 before any implementation begins
- ActivityLog entity with JSONB change payload + Opportunity.Amount field, registered in TenantDbContext with dashboard indexes, generating Phase5_ActivityReporting EF migration
- EF Core SaveChanges interceptor auto-captures all tenant entity changes to ActivityLog, plus paginated GET /api/leads/{leadId}/activity timeline and POST notes endpoint
- Three LINQ-based dashboard endpoints for pipeline overview, conversion funnel, and agent performance — with preset period shortcuts, date range filtering, and sortable results
- 1. [Rule 1 - Bug] ActivityLog.ActorId has non-nullable FK constraint to Users table

---
