# Project Research Summary

**Project:** IronMonkey v1.2 (Admin UI Foundation)
**Domain:** Blazor Server admin dashboard for multi-tenant .NET Aspire CRM
**Researched:** 2026-03-27
**Confidence:** HIGH (verified against .NET 10.0 official docs, Blazor Server multi-tenant patterns, and current 2026 community guidance)

## Executive Summary

IronMonkey v1.2 adds a Blazor Server admin UI to the existing .NET Aspire backend with zero new external NuGet dependencies. The stack adds three service registrations (typed HttpClient, custom AuthenticationStateProvider, CascadingAuthenticationState) and Tailwind CSS v4 standalone binary (no npm). The critical challenge is not technology, but security: Blazor Server circuits are stateful and vulnerable to silent user identity changes on reconnect, stale token expiration during circuit disconnections, and cross-tenant data leakage if DbContext pooling is used or tenant validation is omitted from API endpoints. The recommended approach is to ship Phase 1 (Foundation) with bulletproof authentication guards—circuit identity validation, token refresh handling, and enforced multi-tenant isolation—before building any admin pages. This prevents data leaks and regulatory violations that would require architectural redesign. Phases 2-7 (Recipe/Tenant/User management UIs) depend entirely on Phase 1's authentication infrastructure being solid.

## Key Findings

### Recommended Stack

**No new NuGet packages required.** All capabilities ship with .NET 10.0 and existing IronMonkey dependencies:

**Core technologies:**
- **ASP.NET Core 10.0.5 AuthenticationStateProvider** — Blazor authentication state management. Custom implementation extracts JWT claims from API `/auth/me` endpoint and provides to components via `GetAuthenticationStateAsync()`.
- **System.Security.Claims (10.0 framework)** — ClaimsPrincipal + ClaimsIdentity for building auth state from JWT claims (sub, email, tenant_id, roles).
- **[Authorize] attribute + AuthorizeView component (10.0.5 framework)** — Server-side page protection and conditional UI rendering by role. Enforced on server; cannot be bypassed from browser.
- **IHttpClientFactory + Microsoft.Extensions.ServiceDiscovery (10.1.0 existing)** — Typed HttpClient pattern for API calls with automatic Aspire service discovery (`https+http://apiservice`). Prevents socket exhaustion.
- **Microsoft.Extensions.Http.Resilience (10.1.0 existing)** — Automatic retry + circuit breaker (already in ServiceDefaults). Prevents cascading failures.
- **Tailwind CSS v4 standalone binary (4.x)** — CLI-based CSS generation from .razor files without npm dependency. Downloads from GitHub releases; integrates with MSBuild `<Target>` for automated builds.
- **Razor Components + @page routing (10.0.5 framework)** — Native Blazor component model with built-in page routing and cascading parameters.

**Key architectural additions:** Custom `JwtAuthenticationStateProvider` that polls `/api/auth/me` endpoint to fetch and cache claims per circuit lifetime. Circuit handler (`ICircuitHandler`) to validate user identity on reconnect and reject stale circuits. Bearer token handler to inject JWT from ProtectedSessionStorage into all outgoing HTTP requests. Tailwind CSS content paths configured to include all `.razor` files for class detection.

**Not added:** MudBlazor or Syncfusion (overkill for CRUD pages; use native Razor + Tailwind), component libraries, API documentation tools, i18n frameworks, real-time collaboration, mobile UI, advanced permission granularity, custom report builders, workflow visual builders. All deferred to v2+.

### Expected Features

**Must have (table stakes) for v1.2 Admin UI:**

- **Authentication with JWT + role-based route guards** — Admins log in via `/auth/login`. JWT stored in ProtectedSessionStorage. Routes protected via `@attribute [Authorize(Roles="SuperAdmin")]`. Enforced server-side.
- **Role-Based Access Control (RBAC)** — Three tiers: SuperAdmin (all tenants), TenantAdmin (their tenant only), ConfigAdmin (config-only). JWT includes `tenant_id` and `roles` claims. `AuthorizeView` conditionally renders sections.
- **Navigation shell with sidebar** — Dark gray sidebar (Tailwind), responsive (fixed desktop, collapsible tablet), breadcrumbs for context. Standard SaaS admin dashboard pattern.
- **User management CRUD** — Create, list, edit, deactivate users per tenant. Assign roles via dropdown. View status, created date. Simple form-based entry.
- **Tenant management view** — SuperAdmin table listing all tenants: name, industry recipe applied, status, creation date, last activity. Read-only for v1.2.
- **Audit logging view** — Queryable activity log: actor, action, resource, timestamp, changes. Paginated table, filterable by date/actor/action. No real-time updates in MVP.
- **Form validation and error handling** — Field-level validation feedback. API validation errors displayed per field. Toasts for success/error messages.
- **Logout and session management** — Logout button clears JWT token, redirects to login. Session timeout (15-30 min JWT expiration) with re-auth on 401.
- **Tenant isolation enforcement** — All API calls routed via tenant_id claim. Backend enforces scoping. Admin sees only their tenant's data. SuperAdmin sees all.
- **Recipe management UI** — List recipes (industry, status), create recipe, edit recipe, preview recipe JSON. Deactivate (prevents new tenants from selecting). No bulk application in v1.2.

**Should have (competitive) for v1.2:**

- **Real-time data refresh via SignalR** — Admin sees live updates (new signup approvals, new users, config changes) without manual refresh. Blazor Server uses SignalR natively.
- **Configuration wizards (multi-step forms)** — Complex setups broken into 3-5 steps with progress bar. Example: 5-step recipe creation (metadata → stages → fields → rules → review).
- **Recipe preview modal** — Before saving recipe, preview how it looks. Tabular display of stages, fields, rules. JSON parsing.
- **Advanced dashboard analytics (SuperAdmin only)** — Tenant health metrics: active users, storage usage, API calls, last login. System-wide trends (new signups, churn).
- **Activity timeline visualization** — Timeline view of admin actions instead of table rows. Filter and drill-down capability.

**Defer to v2+:**

- Bulk operations (approve/reject multiple signups, bulk role assignment)
- Bulk recipe application to multiple tenants
- Advanced permission granularity (per-field, per-row access)
- Custom report builder (use CSV export + external BI tools instead)
- Real-time collaboration in config editing
- Mobile admin app
- Workflow rule visual builder (use form-based config)
- Multi-language admin UI
- Admin notification preferences
- Undo/redo for admin actions

### Architecture Approach

Blazor Server admin UI integrates with existing Minimal API backend via typed HttpClient services with JWT authentication. The architecture has three layers: **Authentication Layer** (CustomAuthenticationStateProvider extracts JWT claims from `/api/auth/me`, CascadingAuthenticationState cascades auth context to all components), **HTTP Client Layer** (typed services like RecipeApiClient, TenantApiClient encapsulate API calls with ProtectedSessionStorage token injection), and **UI Layer** (Razor pages with `@attribute [Authorize]` decorators, `AuthorizeView` guards for conditional rendering). All multi-tenant isolation is enforced server-side: tenant_id claim extracted from JWT, tenant_id used to route to correct database via IUserContext, global EF Core query filters enforce tenant scoping automatically. No backend changes required—all existing Minimal API endpoints support the admin UI calls directly.

**Major components:**

1. **CustomAuthenticationStateProvider** — Fetches current user claims from `/api/auth/me` endpoint via HttpClient. Caches claims for circuit lifetime. Provides ClaimsPrincipal to `AuthorizeView` and authorization checks.
2. **JwtAuthenticationStateProvider (variant)** — Alternative implementation that parses JWT directly instead of calling API endpoint. Less secure if JWT is tampered; not recommended.
3. **Typed HttpClient Services** (RecipeApiClient, TenantApiClient, UserApiClient, ConfigurationApiClient) — Encapsulate API calls with DTOs. Bearer token automatically injected by custom DelegatingHandler.
4. **Custom DelegatingHandler** — Reads JWT from ProtectedSessionStorage, attaches to Authorization header before each request. Intercepts 401 Unauthorized, refreshes token via `/api/auth/refresh`, retries request.
5. **Blazor Server Pages** (RecipeList.razor, TenantApprovalForm.razor, UserList.razor, etc.) — Page-level `@attribute [Authorize(Roles="...")]` enforces route protection. `AuthorizeView` conditionally renders content. Components inject ApiClient services and call typed methods.
6. **MainLayout.razor** — Sidebar navigation with role-based links. Admin menu items show only if user has SuperAdmin or TenantAdmin role. Top navigation shows current tenant context.
7. **Circuit Handler** — Validates user identity on circuit reconnect. Rejects or resets circuit if user differs from initial connection. Prevents cross-user contamination.
8. **Tailwind CSS** — Standalone CLI (v4) configured with MSBuild target to run on every build. Content paths scan all `.razor` files for class usage. Output CSS auto-minified in production.

### Critical Pitfalls

1. **Silent User Identity Changes on Circuit Reconnect** — Blazor Server circuit loses connection, reconnects under different user's authentication cookie. Admin editing Tenant A's recipes suddenly executes under Tenant B's identity. **Prevention:** Implement `ICircuitHandler` to validate user identity on reconnect; reject circuit if user differs from initialization; store user ID + tenant ID in circuit state; log all circuit creation/reconnection events for audit trail; test explicit identity switching scenarios.

2. **DbContext Pooling Breaks Multi-Tenant Isolation** — Using `AddDbContextPool` (performance optimization) reuses TenantDbContext instances across requests without re-evaluating tenant context. Tenant A's circuit queries Tenant B's database. **Prevention:** Use `AddDbContext` (not pooling) for TenantDbContext; if pooling required, implement custom factory validating tenant before reuse; add integration tests verifying each request gets fresh context with correct connection string; document in CLAUDE.md: "Never use AddDbContextPool with TenantDbContext."

3. **JWT Token Expiration + Circuit Reconnect = Silent API Failures** — JWT expires while circuit is inactive. On reconnect, HttpClient uses stale token. API returns 401 Unauthorized, but circuit doesn't refresh token automatically. Admin sees generic "Connection lost" instead of "Please re-authenticate." **Prevention:** Implement custom DelegatingHandler intercepting 401 responses, attempting token refresh via `/api/auth/refresh`, retrying request; store refresh token separately (httpOnly cookie); validate token expiration on circuit init + periodically in AuthenticationStateProvider; set JWT expiration 15-30 min, refresh token 7 days; test with artificially expired JWT to verify refresh occurs.

4. **HttpClient Missing JWT Token in Authorization Header** — Blazor stores JWT in ProtectedSessionStorage. Component calls `httpClient.GetAsync("/api/recipes")` without attaching token. API rejects with 401. Admin can log in but can't access any functionality. **Prevention:** Create custom DelegatingHandler (or configure IHttpClientFactory) that reads token from ProtectedSessionStorage and adds Authorization header; register handler with typed HttpClient; test via DevTools Network tab verifying Authorization header is present; integration test assertion: `request.Headers.Authorization.Scheme == "Bearer"`.

5. **Tenant Context Not Validated in API Endpoints** — API endpoint doesn't validate tenant_id from JWT claims. Returns all recipes or defaults to Tenant 0. Admin sees cross-tenant data. **Prevention:** Every multi-tenant endpoint must extract tenant from JWT, validate presence, filter all queries by tenant; use IUserContext service (IronMonkey pattern) to abstract extraction; add integration tests calling endpoint with Tenant A's token, verify only Tenant A's data returned; cross-tenant test: call endpoint with Tenant A's token requesting Tenant B's resource, verify 403 Forbidden; code review checklist required.

6. **Tailwind CSS Hot Reload Doesn't Work with Dotnet Watch** — Changing Tailwind classes in `.razor` doesn't update styles in browser. MSBuild isn't invoked by dotnet watch, so Tailwind CLI doesn't rerun. Old CSS persists. **Prevention:** Document in CLAUDE.md: "During development, run TWO terminals: `dotnet watch` in one, `tailwindcss --watch` in another"; create Makefile or script running both in parallel; or configure MSBuild to always run Tailwind (trades rebuild time for consistency); test by changing Tailwind class, verifying CSS file contains updated class, browser loads new CSS via DevTools Network tab.

7. **Multiple Blazor Circuits for Same User = Stale State + Memory Leaks** — Admin opens admin UI in two browser tabs. Each tab creates separate circuit on server. Tab1 updates recipe, Tab2 doesn't see change (stale state). Each circuit holds memory (DbContext, services, component state). Long-lived admin sessions accumulate memory. **Prevention:** Implement `ICircuitHandler` limiting circuits per user (reject/warn if user has 2+ circuits); or broadcast state changes via SignalR to invalidate caches across circuits; document warning in UI if same user logged in multiple places; prefer SPA-style navigation (single circuit, client-side routing) over tabs; monitor CircuitCount/UserCount ratio; integration tests verifying max 1 circuit per user.

## Implications for Roadmap

Based on research, IronMonkey v1.2 should organize around authentication-first phases with security as the hardening gate between foundation and feature phases.

### Phase 1: Blazor Auth Guards & Token Refresh
**Rationale:** Authentication security is the pre-requisite for all admin features. Must prevent circuit identity changes, token expiration failures, and tenant data leaks before building any admin pages. Foundation prevents regulatory violations that would require architectural rewrites.

**Delivers:**
- CustomAuthenticationStateProvider (fetches claims from `/api/auth/me`, caches per circuit)
- ICircuitHandler validating user identity on reconnect (rejects stale circuits)
- Custom DelegatingHandler for bearer token injection (reads from ProtectedSessionStorage)
- Token refresh handler intercepting 401, calling `/api/auth/refresh`, retrying request
- Integrated startup tests verifying DI resolution (DbContext uses AddDbContext, not pooling)
- Integration tests for circuit identity mismatch, token expiration, cross-tenant isolation

**Addresses features:**
- Authentication with JWT + role-based route guards (infrastructure complete)
- Session management and logout (token refresh + logout endpoints)
- Tenant isolation enforcement (validation layer in place)

**Avoids pitfalls:**
- Pitfall 1 (silent user identity changes) — circuit identity validation implemented
- Pitfall 2 (DbContext pooling) — locked to AddDbContext, never pooling
- Pitfall 3 (token expiration on reconnect) — token refresh handler + refresh logic
- Pitfall 4 (missing JWT in header) — DelegatingHandler automatically injects token
- Pitfall 7 (tenant validation) — API still enforces (backend pattern holds)

**Risk:** Token refresh logic must be bulletproof; any edge cases cause 401 cascades. Test with real network delays and circuit disconnection scenarios.

### Phase 2: API Client Foundation & Typed Services
**Rationale:** Phase 1 ensures auth works. Phase 2 builds the API abstraction layer that all pages depend on. Typed client services (RecipeApiClient, TenantApiClient, etc.) enable clean component code and centralized error handling.

**Delivers:**
- Typed HttpClient services for each admin feature (Recipes, Tenants, Users, Configuration)
- DTO models matching API contracts (RecipeResponse, TenantResponse, UserResponse, etc.)
- Centralized error handling for 401/403/validation errors
- Integration tests for each service: successful calls, 401 on stale token, 403 on cross-tenant access
- Service discovery integration verified (Aspire `https+http://apiservice` resolution)

**Addresses features:**
- User management CRUD (UserApiClient)
- Tenant management view (TenantApiClient)
- Recipe management UI (RecipeApiClient)
- Form validation and error handling (services return ValidationError responses)

**Uses stack:**
- IHttpClientFactory with Aspire service discovery
- System.Net.Http.Json for typed deserialization

**Risk:** API contracts must be finalized; breaking changes require service updates. Document DTO ownership upfront (API is source-of-truth).

### Phase 3: Blazor UI Foundation & Tailwind Setup
**Rationale:** Auth (Phase 1) and APIs (Phase 2) complete. Phase 3 builds the UI shell: MainLayout, sidebar navigation, Tailwind CSS integration, App.razor with CascadingAuthenticationState.

**Delivers:**
- MainLayout.razor with responsive sidebar (Tailwind grid layout)
- App.razor wrapping routes with CascadingAuthenticationState
- Tailwind CSS standalone binary integrated with MSBuild target
- `wwwroot/css/input.css` importing Tailwind, `output.css` generated
- Development setup documented in CLAUDE.md (two-terminal guidance)
- Integration test verifying CSS output contains expected classes

**Addresses features:**
- Navigation shell with sidebar (UI complete)
- Form validation and error handling (base styling + component pattern)
- Logout and session management (Logout button in navbar)

**Avoids pitfalls:**
- Pitfall 5 (Tailwind hot reload) — documented and tested
- Pitfall 10 (content paths miss .razor files) — config verified before any components

**Risk:** Tailwind standalone binary requires download and PATH setup. Provide script or documented fallback.

### Phase 4: Login Page & Authentication Flow
**Rationale:** Auth guards (Phase 1), API layer (Phase 2), UI shell (Phase 3) complete. Phase 4 implements the user-facing login page and tests end-to-end auth flow.

**Delivers:**
- LoginPage.razor with email/password form
- Form validation (email format, password required)
- API call to `/auth/login`, receives JWT
- JWT stored in ProtectedSessionStorage
- Redirect to dashboard on success, error message on failure
- Integration tests: successful login, invalid credentials, token storage verification
- Logout button in MainLayout, clears token, redirects to login

**Addresses features:**
- Authentication with JWT + role-based route guards (user-facing)
- Logout and session management (complete)

**Risk:** ProtectedSessionStorage requires Blazor JavaScript interop; ensure JS files are present. Test logout edge case where token is manually cleared.

### Phase 5: Recipe Management UI (CRUD Pages)
**Rationale:** Phases 1-4 provide auth, APIs, UI shell, and login. Phase 5 implements the first full feature: recipe management (RecipeList, RecipeCreate, RecipeEdit, preview modal). Demonstrates full CRUD + form validation pattern for reuse in subsequent phases.

**Delivers:**
- RecipeList.razor — paginated table with industry, status, edit/delete buttons
- RecipeCreate.razor — form for new recipe (metadata, stages, fields, rules, roles)
- RecipeEdit.razor — edit existing recipe with change tracking
- RecipePreview.razor — modal showing recipe JSON as tables (stages, fields, rules)
- Integration tests: list recipes, create with validation errors, edit, deactivate
- SignalR integration for real-time refresh (if list is updated, other admins see change live)

**Addresses features:**
- Recipe management UI (complete)
- Real-time data refresh via SignalR (optional for MVP; add if time permits)
- Configuration wizards (multi-step form for recipe creation; optional for MVP)

**Uses architecture:**
- RecipeApiClient (Phase 2)
- Form validation pattern from Phase 3
- Auth guards from Phase 1

**Risk:** Recipe JSON structure must align with backend model. Serialization errors on preview. Test with realistic recipe data (10+ stages, 20+ fields).

### Phase 6: Tenant & User Management UIs
**Rationale:** Recipe management pattern (Phase 5) established. Phase 6 applies same pattern to tenant and user management: list tables, approve/reject forms, role assignment.

**Delivers:**
- TenantList.razor — SuperAdmin view of all tenants
- SignupRequestList.razor + ApprovalForm.razor — approve/reject pending signups
- UserList.razor — list users per tenant
- UserCreate.razor — form to add users with role assignment
- RoleAssignment.razor — change user roles
- Integration tests for each page, cross-tenant isolation tests

**Addresses features:**
- User management CRUD (complete)
- Tenant management view (complete)
- Role-Based Access Control RBAC (UI enforcement)

**Risk:** Signup approval workflow must be atomic (transaction wrapping); test partial failure scenarios. Cross-tenant isolation test: verify TenantAdmin can only see their tenant's users.

### Phase 7: Audit Logging & Dashboard Views
**Rationale:** Phases 1-6 complete all core admin functions. Phase 7 adds observability: audit log table, activity timeline, health dashboard (SuperAdmin only).

**Delivers:**
- AuditLogView.razor — queryable activity table (actor, action, resource, timestamp, changes)
- ActivityTimeline.razor — timeline visualization of admin actions
- AdminDashboard.razor (SuperAdmin) — tenant health metrics (active users, API calls, last activity), system-wide trends (new signups, churn)
- Pagination + filtering for large log tables
- Integration tests: audit log filtering, data retention (old entries not shown)

**Addresses features:**
- Audit logging view (complete)
- Advanced dashboard analytics (SuperAdmin only, optional for MVP)
- Activity timeline visualization (optional for MVP)

**Avoids pitfalls:**
- None specific to Phase 7; relies on Phases 1-6 being solid

**Risk:** Querying across all tenant databases for dashboard metrics may be slow. Recommend caching (hourly) or materialized views for scale.

### Phase 8: Polish & Performance Hardening
**Rationale:** All features implemented. Phase 8 hardens performance, fixes edge cases, verifies multi-circuit state consistency.

**Delivers:**
- Performance profiling: form rendering time, table scroll performance, circuit memory usage
- Virtualize component for large tables (render only visible rows)
- Circuit state tests: verify no stale state across multiple tabs
- Error boundary components for graceful crash handling
- Load tests: 100+ concurrent admins, verify circuit limits + memory
- Security audit: verify no XSS, no CSRF, no token leakage

**Addresses features:**
- Form validation and error handling (edge cases)
- Real-time data refresh (load test SignalR scalability)

**Avoids pitfalls:**
- Pitfall 6 (multiple circuits = stale state) — circuit state tests + monitoring
- Pitfall 9 (form performance with 200+ fields) — Virtualize + pagination
- Pitfall 11 (form state lost on reconnect) — state persistence tests

**Risk:** Performance issues may require refactoring large forms. Test with realistic data volumes early (Phase 5, not Phase 8).

### Phase Ordering Rationale

1. **Foundation first (Phase 1):** Authentication security is the hardening gate. No admin UI works without bulletproof token management and circuit identity validation. Pitfalls here are regulatory violations; must be right before building anything user-facing.

2. **APIs before UI (Phase 2):** Typed HttpClient services are pre-requisite for Phase 3-7. Services encapsulate error handling + validation response parsing; components don't repeat this logic.

3. **UI shell before pages (Phase 3):** MainLayout, Tailwind, CascadingAuthenticationState are dependencies for all pages. Centralize styling, navigation, auth cascading here.

4. **Login before features (Phase 4):** Login page is the entry point. Must be tested end-to-end before building admin pages that require auth.

5. **Recipe management as proof-of-concept (Phase 5):** Recipes are complex (multi-step wizard, JSON preview, real-time refresh). Implement first to validate full CRUD + form patterns before replicating in tenant/user management.

6. **Tenant/user management follows recipe pattern (Phase 6):** Less complex; reuses patterns from Phase 5.

7. **Observability last (Phase 7):** Audit logs and dashboards depend on all features working. Build features first, add observability after.

8. **Polish last (Phase 8):** Performance, edge cases, security hardening come after features work.

9. **Not in v1.2:** Bulk operations, real-time collaboration, advanced permissions, mobile UI, i18n, undo/redo, visual workflow builders. All deferred to v2+.

### Research Flags

Phases likely needing deeper research during planning:

- **Phase 5 (Recipe Management UI):** Recipe JSON structure must align with backend model. Validate that serialization/deserialization round-trips correctly. May need backend API changes for preview endpoint if not already present. Recommend 1-2 API contract reviews with backend team.

- **Phase 7 (Audit & Dashboard):** Querying across all tenant databases for system-wide metrics is non-trivial. Recommend researching materialized views, caching strategy, or pre-computation approach during planning.

Phases with standard patterns (skip `/gsd:research-phase`):

- **Phase 1 (Auth Guards):** Circuit handlers, token refresh, bearer token injection are well-documented in Blazor Server literature. No novel patterns.

- **Phase 2 (API Client):** Typed HttpClient services are standard ASP.NET Core pattern. No research needed.

- **Phase 3 (UI Foundation):** Tailwind CSS standalone, Blazor MainLayout, CascadingAuthenticationState all have official docs and community examples. Standard patterns.

- **Phase 4 (Login):** Standard Blazor form pattern. No research needed.

- **Phase 6 (Tenant/User Management):** Reuses recipe management pattern from Phase 5. No research needed.

- **Phase 8 (Polish):** Performance profiling and Virtualize component are documented. No research needed.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All recommendations verified against .NET 10.0 official docs (authentication, Blazor Server, HttpClient), existing IronMonkey dependencies (Aspire 13.1.0, ServiceDefaults). No new NuGet packages; pure framework capabilities. Tailwind v4 standalone verified against GitHub releases and 2026 community guidance. |
| Features | HIGH | Table stakes (auth, RBAC, navigation, CRUD) verified against SaaS admin dashboard best practices (HubSpot, Pipedrive admin UIs, Stripe dashboard). Differentiators (real-time, wizards, bulk ops) aligned with 2026 Blazor Server community patterns. Anti-features clearly scoped (mobile, i18n, collaboration deferred). |
| Architecture | HIGH | Multi-tenant isolation via JWT + IUserContext verified against existing IronMonkey v1.0-v1.1 backend. Blazor Server circuit lifecycle, SignalR integration, authentication flow all verified against official .NET docs and validated against codebase inspection. No breaking changes to existing API required. |
| Pitfalls | MEDIUM-HIGH | 11 pitfalls extracted from Blazor Server GitHub issues (#65272, #64607), official Microsoft Learn guidance, and 2026 community blog posts (DEV Community, Medium). Pitfalls 1-4 (auth-related) verified against official async docs. Pitfalls 5-11 (UI/styling/state) verified against community patterns. Some pitfalls (e.g., Tailwind content path issues) are implementation-specific, not architectural. |

**Overall confidence:** HIGH

### Gaps to Address

1. **Recipe JSON Serialization Round-Trip (Phase 5):** Recipe definition stored as JSONB in central database. Blazor preview modal deserializes JSON to display as tables (stages, fields, rules). **How to handle:** Phase 5 planning should validate that backend's recipe JSON structure matches Blazor deserialization expectations. Add round-trip test: serialize recipe to JSON, deserialize in Blazor, serialize again, verify match.

2. **System-Wide Metrics Query Performance (Phase 7):** Dashboard querying all tenant databases for active user counts, API call totals, storage usage. With 100+ tenants, this could be slow. **How to handle:** Phase 7 planning should research materialized views (PostgreSQL) or pre-computation (Hangfire job aggregating metrics hourly). Recommend caching for 1-hour granularity to avoid real-time queries.

3. **JWT Refresh Token Strategy:** Research assumes `/api/auth/refresh` endpoint exists. Need to verify backend supports refresh tokens and refresh endpoint. **How to handle:** Phase 1 planning should verify backend has RefreshTokenEndpoint or similar. If missing, add to API roadmap (may be blocking). Recommend storing refresh token in httpOnly cookie (more secure than ProtectedSessionStorage).

4. **Circuit Identity Validation Edge Cases (Phase 1):** ICircuitHandler can reject stale circuits, but user experience on rejection is unclear. Should rejected circuit show error? Redirect to login? Silent? **How to handle:** Phase 1 planning should decide error handling strategy. Recommend: log warn-level entry, redirect user to login page with message "Your session has been invalidated for security reasons. Please log in again."

5. **Form State Persistence Across Reconnects (Phase 5-6):** Research indicates forms lose state on circuit disconnect > reconnect. ProtectedSessionStorage can persist form state, but implementation details unclear. **How to handle:** Phase 5 planning should implement form state auto-save to localStorage on every change. On component init, restore from localStorage if available. Test with artificial circuit disconnect to verify state persists.

6. **SignalR Hub Authorization (Phase 5 & 7):** Phases 5 and 7 use real-time updates via IHubContext (SignalR). JWT claims must be validated for hub clients. **How to handle:** Research assumes JWT is already validated by ASP.NET Core middleware. Phase 5 planning should verify that hub clients are authenticated before receiving updates. Recommend using SignalR's `[Authorize(Roles="...")]` attributes on hub methods.

## Sources

### Primary (HIGH confidence)

- **STACK_ADMIN_UI.md** — Blazor Server authentication, HttpClient patterns, Tailwind v4 standalone CLI setup (verified against Microsoft Learn, official Tailwind docs, 2026 community guidance)
- **ARCHITECTURE.md** — Blazor Server integration with .NET Aspire backend, circuit lifecycle, component boundaries, data flow (verified against codebase inspection, official ASP.NET Core docs)
- **FEATURES.md** — Admin UI table stakes, differentiators, anti-features (verified against HubSpot/Pipedrive/Salesforce admin UIs, SaaS admin dashboard patterns)
- **PITFALLS_BLAZOR_UI.md** — 11 critical/moderate/minor pitfalls with prevention strategies (verified against GitHub issues #65272, #64607, official docs, 2026 community blog posts)

### Secondary (MEDIUM confidence)

- ASP.NET Core Blazor authentication and authorization (.NET 10.0) — Microsoft Learn official docs
- Tailwind CSS v4 standalone CLI — Official GitHub releases, DEV Community articles
- Multi-tenancy with EF Core in Blazor Server — Developer for Life blog, Medium articles
- Blazor Server circuit lifecycle and SignalR — GitHub aspnetcore issues, community forums
- JWT token refresh patterns — Auth0, developer.okta.com, Medium articles

### Tertiary (LOW confidence)

- Specific UI patterns from Stripe/HubSpot admin dashboards — observed, not formally documented
- Performance metrics (form lag at 200+ fields, circuit memory overhead) — community reports, not empirically validated on IronMonkey codebase
- JWT refresh token storage strategy (httpOnly cookie vs ProtectedSessionStorage) — security recommendations vary; choice depends on IronMonkey's risk tolerance

---

*Research completed: 2026-03-27*
*Ready for roadmap: yes*
