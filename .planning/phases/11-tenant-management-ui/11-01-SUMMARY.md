---
phase: 11-tenant-management-ui
plan: 01
subsystem: api
tags: [dotnet, aspire, efcore, minimal-api, postgresql, integration-tests]

# Dependency graph
requires:
  - phase: 01-multi-tenancy-foundation
    provides: CentralDbContext with Tenant and SignupRequest entities, TenantProvisioningService, test infrastructure

provides:
  - GET /admin/tenants endpoint returning paginated TenantSummary list ordered by CreatedAt desc
  - GET /admin/signup/{id} endpoint returning full SignupRequestDetail or 404
  - Both endpoints registered under /admin group with RequireAuthorization
  - Integration tests covering list, sort order, field content, happy-path detail, and 404 cases

affects: [11-tenant-management-ui]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Internal HandleForTest method pattern for testing private Minimal API handlers directly
    - TenantSummary / SignupRequestDetail read-model records for projection without exposing entity internals

key-files:
  created:
    - IronMonkey.ApiService/Authentication/Endpoints/ListTenantsEndpoint.cs
    - IronMonkey.ApiService/Authentication/Endpoints/GetSignupRequestEndpoint.cs
    - IronMonkey.Tests/Integration/TenantManagement/ListTenantsEndpointTests.cs
    - IronMonkey.Tests/Integration/TenantManagement/GetSignupRequestEndpointTests.cs
  modified:
    - IronMonkey.ApiService/Endpoints.cs

key-decisions:
  - "HandleForTest internal static method used on endpoint classes to enable direct handler invocation in integration tests without WebApplicationFactory"
  - "SignupRequestDetail uses IndustryType field (actual entity property) not RecipeId as plan interface section incorrectly stated"
  - "TenantSummary omits AppliedRecipeId — Tenant entity does not have that property in codebase; field list trimmed to actual available fields"
  - "Duplicate MapIngestionEndpoints method auto-removed from Endpoints.cs (pre-existing bug)"

patterns-established:
  - "Endpoint HandleForTest: static internal method delegating to private Handle — enables integration tests to call handler directly with injected DbContext"

requirements-completed: [TNUI-01, TNUI-02]

# Metrics
duration: 15min
completed: 2026-04-01
---

# Phase 11 Plan 01: Tenant Management UI Backend Endpoints Summary

**GET /admin/tenants and GET /admin/signup/{id} Minimal API endpoints with TenantSummary/SignupRequestDetail projections and 5 passing integration tests**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-04-01T14:50:00Z
- **Completed:** 2026-04-01T15:06:10Z
- **Tasks:** 2 (TDD task + registration task)
- **Files modified:** 5

## Accomplishments
- Created `ListTenantsEndpoint` — GET /admin/tenants returning `List<TenantSummary>` ordered by `CreatedAt` descending
- Created `GetSignupRequestEndpoint` — GET /admin/signup/{id} returning `SignupRequestDetail` or 404
- Registered both endpoints in `MapPlatformAdminEndpoints` under `/admin` group (RequireAuthorization)
- 5 integration tests covering: list returns sorted results, empty list, field content validation, happy-path detail, and 404 not-found

## Task Commits

Each task was committed atomically:

1. **Task 1: Create endpoints and integration tests** - `8754549` (feat)
2. **Task 2: Register endpoints in Endpoints.cs** - `7820ee6` (feat)

## Files Created/Modified
- `IronMonkey.ApiService/Authentication/Endpoints/ListTenantsEndpoint.cs` - GET /admin/tenants handler with TenantSummary projection
- `IronMonkey.ApiService/Authentication/Endpoints/GetSignupRequestEndpoint.cs` - GET /admin/signup/{id} handler with SignupRequestDetail projection and 404
- `IronMonkey.Tests/Integration/TenantManagement/ListTenantsEndpointTests.cs` - 3 tests (sorted list, empty, field content)
- `IronMonkey.Tests/Integration/TenantManagement/GetSignupRequestEndpointTests.cs` - 2 tests (detail fields, 404)
- `IronMonkey.ApiService/Endpoints.cs` - Added `MapEndpoint<GetSignupRequestEndpoint>()` and `MapEndpoint<ListTenantsEndpoint>()`

## Decisions Made
- Used `internal static HandleForTest` pattern on endpoint classes to expose private handlers for direct invocation in integration tests — avoids the complexity of `WebApplicationFactory` while still hitting real PostgreSQL
- Used `IndustryType` (actual field on `SignupRequest`) instead of `RecipeId` referenced in plan's interface section (plan's interface was describing a future entity shape, not the current one)
- Omitted `AppliedRecipeId` from `TenantSummary` — the `Tenant` entity does not have this property; field list trimmed to actual available properties

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Removed duplicate MapIngestionEndpoints method in Endpoints.cs**
- **Found during:** Task 1 (initial build attempt)
- **Issue:** `Endpoints.cs` contained two identical `MapIngestionEndpoints` private methods, causing CS0111 "Type already defines a member" compile error
- **Fix:** Removed the second (duplicate) `MapIngestionEndpoints` block at lines 182-202
- **Files modified:** `IronMonkey.ApiService/Endpoints.cs`
- **Verification:** `dotnet build IronMonkey.ApiService` exits 0 after removal
- **Committed in:** `8754549` (Task 1 commit)

**2. [Rule 1 - Bug] Plan interface section listed incorrect entity fields**
- **Found during:** Task 1 (reading actual entity files)
- **Issue:** Plan's `<interfaces>` section listed `RecipeId` on `SignupRequest` and `AppliedRecipeId` on `Tenant`, neither of which exist in the actual entity definitions
- **Fix:** Implemented endpoints using actual available fields (`IndustryType` on SignupRequest; omit `AppliedRecipeId` from TenantSummary)
- **Files modified:** `GetSignupRequestEndpoint.cs` (uses IndustryType), `ListTenantsEndpoint.cs` (no AppliedRecipeId)
- **Verification:** All 5 integration tests pass asserting actual field values
- **Committed in:** `8754549` (Task 1 commit)

---

**Total deviations:** 2 auto-fixed (2 Rule 1 bugs)
**Impact on plan:** Both auto-fixes required for correctness. Build would not compile without fix #1. Implementation would be incorrect without fix #2. No scope creep.

## Issues Encountered
- Plan's interface section referenced entity properties (`RecipeId`, `AppliedRecipeId`) that don't exist in the codebase — corrected by reading actual entity files before writing implementation.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Both admin read endpoints available for UI layer in plan 11-02
- GET /admin/tenants returns `Id, Name, Status, SubscriptionPlan, IsProvisioned, CreatedAt`
- GET /admin/signup/{id} returns `Id, CompanyName, AdminEmail, Phone, CompanySize, Address, BillingContact, IndustryType, Status, ReviewNote, CreatedAt`
- Both registered under `/admin` group with `RequireAuthorization` — UI must pass JWT with admin role

---
*Phase: 11-tenant-management-ui*
*Completed: 2026-04-01*
