---
gsd_state_version: 1.0
milestone: v1.2
milestone_name: Admin UI
status: Ready to execute
stopped_at: Completed 12-01-PLAN.md - User management API endpoints
last_updated: "2026-04-01T18:03:07.732Z"
progress:
  total_phases: 5
  completed_phases: 3
  total_plans: 12
  completed_plans: 10
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-27)

**Core value:** Any business can configure their complete lead management workflow without writing code
**Current focus:** Phase 12 — user-role-management-ui

## Current Position

Phase: 12 (user-role-management-ui) — EXECUTING
Plan: 2 of 3

## Performance Metrics

**v1.0:** 5 phases, 30 plans, 59 tasks — shipped 2026-03-24
**v1.1:** 3 phases, 9 plans, 16 tasks — shipped 2026-03-27
**v1.2:** 5 phases planned, 0 plans complete

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.

Key decisions for v1.2:

- Tailwind CSS via standalone CLI (no Node.js dependency)
- Admin UI first; end-user CRM pages deferred to v1.3
- No MudBlazor or Syncfusion — native Razor + Tailwind only
- JWT stored in ProtectedSessionStorage; custom DelegatingHandler injects token
- [Phase 09-ui-foundation-auth]: System.IdentityModel.Tokens.Jwt 8.7.0 added to Web project for client-side JWT claims parsing without signature validation
- [Phase 09-ui-foundation-auth]: AdminAuthenticationStateProvider registered as both concrete Scoped and AuthenticationStateProvider interface — allows LoginAsync/LogoutAsync injection alongside Blazor auth cascade
- [Phase 09-ui-foundation-auth]: ProtectedSessionStorage key 'auth_token' as canonical JWT storage key used by both AdminAuthenticationStateProvider and BearerTokenHandler
- [Phase 09-ui-foundation-auth]: Tailwind v4 MSBuild target uses BeforeTargets=ResolveStaticWebAssets to ensure output.css exists before Aspire static asset validation
- [Phase 09-ui-foundation-auth]: Aspire.Hosting.PostgreSQL 9.1.0 added to AppHost — was missing, causing AddPostgres compile error
- [Phase 09-ui-foundation-auth]: AuthorizeRouteView with NotAuthorized block for router-level auth guard — Nav.NavigateTo('/login') in NotAuthorized fires before any page renders
- [Phase 09-ui-foundation-auth]: Admin/Index.razor stat cards use em-dash placeholders — Phase 10-13 will wire real API counts
- [Phase 10-recipe-management-ui]: Added IronMonkey.Data project reference to IronMonkey.Web — required for RecipeContentModel types used by recipe management UI
- [Phase 10-recipe-management-ui]: RecipeContentEditor uses native input with @onchange for comma-separated options rather than InputText — List<string> not directly bindable
- [Phase 10-recipe-management-ui]: NavigateToPreview/NavigateToEdit as named methods — Razor HTML attributes cannot contain interpolated strings inline
- [Phase 10-recipe-management-ui]: RecipeListItem as mutable class (not record) to support optimistic IsActive=false mutation
- [Phase 10]: Dual API fetch on RecipeEdit init merges content from GET /api/recipes/{id} with metadata from list endpoint — no single endpoint provides all fields
- [Phase 10]: RecipeListItem as record enables 'with' expression for optimistic version update after save response
- [Phase 11-tenant-management-ui]: HandleForTest internal static method pattern used on endpoint classes for integration testing without WebApplicationFactory
- [Phase 11-tenant-management-ui]: SignupRequestDetail uses IndustryType not RecipeId — plan interface section referenced non-existent entity field
- [Phase 11-tenant-management-ui]: SignupListItem.IsProvisioned tracked locally (false initially); Plan 11-03 sets true after provision — avoids changing list endpoint
- [Phase 11-tenant-management-ui]: Modal stubs (OpenApproveModal, OpenRejectModal, RetryProvisionAsync) created in TenantManagement.razor so Plan 11-03 has clean integration surface
- [Phase 11]: Handle methods on ApproveTenantEndpoint and RejectTenantEndpoint changed from private to internal with InternalsVisibleTo for direct integration test invocation
- [Phase 11]: Two-step approve+provision chain: approve first then provision, partial failure shows retry button (D-17)
- [Phase 12-user-role-management-ui]: IgnoreQueryFilters used on all user queries to include deactivated users — global query filter excludes IsDeleted=true by default

### Pending Todos

None.

### Blockers/Concerns

- Research flag: JWT refresh token endpoint (`/api/auth/refresh`) may not exist in backend — verify during Phase 9 planning
- Research flag: Recipe JSON serialization round-trip must be validated during Phase 10 planning

## Session Continuity

Last session: 2026-04-01T18:03:07.721Z
Stopped at: Completed 12-01-PLAN.md - User management API endpoints
Resume file: None
