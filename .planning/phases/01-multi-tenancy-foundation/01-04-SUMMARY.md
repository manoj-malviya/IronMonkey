---
phase: 01-multi-tenancy-foundation
plan: 04
subsystem: auth
tags: [tenancy, postgresql, efcore, bcrypt, npgsql, provisioning, testcontainers]

# Dependency graph
requires:
  - phase: 01-multi-tenancy-foundation/01-03
    provides: LoginEndpoint, UserTenantIndex, CentralDbContext with SignupRequests/Tenants, ITenantDbContextFactory, TenantDbContext with Users/Roles
  - phase: 01-multi-tenancy-foundation/01-02
    provides: Tenant.MarkProvisioned(), SignupRequest.Create/Approve/Reject/LinkTenant, TenantDbContextFactory, EF migrations
provides:
  - POST /auth/signup — public endpoint to submit tenant signup requests (BCrypt-hashed password, duplicate guard)
  - POST /admin/signup/{id}/approve — admin endpoint to approve pending signup requests
  - POST /admin/signup/{id}/reject — admin endpoint to reject pending signup requests
  - GET /admin/signup — admin endpoint to list signup requests with optional status filter
  - POST /admin/tenants/{id}/provision — admin endpoint triggering isolated DB creation + migration + seed
  - ITenantProvisioningService interface and TenantProvisioningService implementation
  - TenantProvisioningService: creates PostgreSQL DB via NpgsqlConnection, applies MigrateAsync(), seeds admin user from signup request
  - 4 integration tests covering full signup-to-provisioned flow using TestContainers PostgreSQL
affects:
  - 01-multi-tenancy-foundation/01-05 (if exists)
  - all phases using tenant-scoped DB operations (provisioning is prerequisite)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Npgsql CREATE DATABASE pattern: direct NpgsqlConnection against pg_database for idempotent DB creation
    - Provisioning orchestration: central DB signup request -> slug-based DB name -> MigrateAsync() -> seed -> MarkProvisioned()
    - Migration-first seeding: roles are seeded by EF migration InsertData; service only inserts users using pre-existing roles
    - Admin endpoint group: /admin prefix with RequireAuthorization() wrapping all platform admin operations

key-files:
  created:
    - IronMonkey.ApiService/Authentication/Endpoints/SignupRequestEndpoint.cs (POST /auth/signup)
    - IronMonkey.ApiService/Authentication/Endpoints/ApproveTenantEndpoint.cs (POST /admin/signup/{id}/approve)
    - IronMonkey.ApiService/Authentication/Endpoints/RejectTenantEndpoint.cs (POST /admin/signup/{id}/reject)
    - IronMonkey.ApiService/Authentication/Endpoints/ListSignupRequestsEndpoint.cs (GET /admin/signup)
    - IronMonkey.ApiService/Authentication/Endpoints/ProvisionTenantEndpoint.cs (POST /admin/tenants/{id}/provision)
    - IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs (ITenantProvisioningService + implementation)
  modified:
    - IronMonkey.ApiService/Endpoints.cs (added MapPlatformAdminEndpoints + signup to public auth group)
    - IronMonkey.ApiService/ConfigureServices.cs (ITenantProvisioningService registered as Scoped)
    - IronMonkey.Tests/Integration/TenantProvisioningTests.cs (4 integration tests replacing Wave-0 stubs)

key-decisions:
  - "Migration-first role seeding: EF tenant migration already seeds all roles via InsertData — service must load role by name from DB rather than re-insert, avoids PK violation on roles table"
  - "TenantProvisioningService uses raw NpgsqlConnection for CREATE DATABASE — EF cannot run DDL cross-database; idempotent via pg_database existence check"
  - "Provisioning endpoint returns 400 BadRequest for InvalidOperationException — clear error for not-approved or already-provisioned states"
  - "Slug-based DB name: ironmonkey_{slug_with_underscores} derived from company name slug for human-readable tenant DB naming"

patterns-established:
  - "Platform admin endpoints: /admin group with RequireAuthorization(), all platform admin ops under this prefix"
  - "Public signup: POST /auth/signup in AllowAnonymous group, BCrypt hash on intake, duplicate guard on non-Rejected emails"

requirements-completed: [TNCY-01]

# Metrics
duration: 7min
completed: 2026-03-20
---

# Phase 1 Plan 4: Tenant Provisioning Workflow Summary

**Signup-to-provision workflow: BCrypt-hashed POST /auth/signup, admin approve/reject endpoints, and TenantProvisioningService creating isolated PostgreSQL databases via NpgsqlConnection with MigrateAsync() and seeded admin user — validated by 4 integration tests using TestContainers**

## Performance

- **Duration:** 7 min
- **Started:** 2026-03-20T10:19:33Z
- **Completed:** 2026-03-20T10:26:53Z
- **Tasks:** 3
- **Files modified:** 9

## Accomplishments
- 5 new endpoints created: POST /auth/signup (public), approve/reject/list (admin), provision (admin)
- TenantProvisioningService: full 7-step provisioning flow — validate signup, create Tenant record, CREATE DATABASE via Npgsql, MigrateAsync() to apply schema, seed admin user, MarkProvisioned, register in UserTenantIndex
- 4 integration tests replaced Wave-0 Fact(Skip) stubs — all pass against real PostgreSQL via TestContainers

## Task Commits

Each task was committed atomically:

1. **Task 1: Signup, approval, rejection, list endpoints** - `921e54a` (feat)
2. **Task 2: TenantProvisioningService and ProvisionTenantEndpoint** - `4d95d11` (feat)
3. **Task 3: TenantProvisioningTests (4 integration tests)** - `db17dba` (test)

## Files Created/Modified
- `IronMonkey.ApiService/Authentication/Endpoints/SignupRequestEndpoint.cs` - POST /auth/signup, BCrypt hash, duplicate email guard
- `IronMonkey.ApiService/Authentication/Endpoints/ApproveTenantEndpoint.cs` - POST /admin/signup/{id}/approve, status guard
- `IronMonkey.ApiService/Authentication/Endpoints/RejectTenantEndpoint.cs` - POST /admin/signup/{id}/reject, status guard
- `IronMonkey.ApiService/Authentication/Endpoints/ListSignupRequestsEndpoint.cs` - GET /admin/signup with optional ?status= filter
- `IronMonkey.ApiService/Authentication/Endpoints/ProvisionTenantEndpoint.cs` - POST /admin/tenants/{id}/provision, catches InvalidOperationException -> 400
- `IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs` - ITenantProvisioningService + 7-step provisioning orchestration
- `IronMonkey.ApiService/Endpoints.cs` - MapPlatformAdminEndpoints() group + signup in public auth group
- `IronMonkey.ApiService/ConfigureServices.cs` - ITenantProvisioningService Scoped DI registration
- `IronMonkey.Tests/Integration/TenantProvisioningTests.cs` - 4 tests replacing Wave-0 stubs

## Decisions Made
- **Migration-first role seeding**: EF tenant migration already seeds all roles via `InsertData`. The service loads the Admin role by name from the already-migrated DB rather than trying to INSERT it again — avoids PK_roles violation on any subsequent provisioning.
- **Raw Npgsql for CREATE DATABASE**: EF Core cannot execute cross-database DDL. `NpgsqlConnection` directly against the central DB server creates the tenant database. Idempotent — checks `pg_database` before creating.
- **Slug-based DB naming**: `ironmonkey_{slug}` where slug is derived from company name (lowercase, spaces->hyphens, hyphens->underscores in DB name) — human-readable and unique per tenant.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed duplicate primary key on roles table during seeding**
- **Found during:** Task 3 (TenantProvisioningTests integration tests running)
- **Issue:** `SeedTenantDataAsync` tried to `db.Roles.Add(Role.Create(201, "Admin"))` but the EF tenant migration's `InsertData` already inserts all roles (id=201 Admin) during `MigrateAsync()`. This caused `23505: duplicate key value violates unique constraint "PK_roles"` on SaveChangesAsync.
- **Fix:** Changed seed logic to `db.Roles.SingleAsync(r => r.Name == "Admin")` — loads the existing Admin role (already seeded by migration) and uses it to create the admin user, without inserting a duplicate
- **Files modified:** `IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs`
- **Verification:** All 4 integration tests pass
- **Committed in:** db17dba (Task 3 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 - Bug, seeding conflict with migration-seeded roles)
**Impact on plan:** Necessary correctness fix. The plan specification assumed roles needed to be seeded but the migration already handles this. No scope creep.

## Issues Encountered
- None beyond the seeding deviation documented above.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- TNCY-01 complete: full signup → approve → provision workflow operational
- Each provisioned tenant has an isolated PostgreSQL database with schema (via MigrateAsync) and admin user seeded
- UserTenantIndex populated on provisioning — LoginEndpoint can now authenticate users in provisioned tenants
- All 4 provisioning integration tests green — contract is verified end-to-end
- Pre-existing AppDbContext deprecation warnings in other endpoints are deferred (out of scope for this plan)

---
*Phase: 01-multi-tenancy-foundation*
*Completed: 2026-03-20*
