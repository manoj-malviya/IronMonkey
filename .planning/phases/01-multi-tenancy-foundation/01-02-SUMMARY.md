---
phase: 01-multi-tenancy-foundation
plan: 02
subsystem: database
tags: [postgresql, entityframeworkcore, npgsql, migrations, multi-tenancy, global-query-filters]

# Dependency graph
requires:
  - phase: 01-multi-tenancy-foundation/01-01
    provides: BaseTenantEntity, Entity abstractions, initial project structure

provides:
  - CentralDbContext with Tenants and SignupRequests tables
  - TenantDbContext with global query filters enforcing TenantId on all BaseTenantEntity types
  - ITenantDbContextFactory / TenantDbContextFactory for per-tenant DB creation
  - SignupRequest entity and EF configuration
  - Extended Tenant entity with approval workflow fields
  - EF Core migrations for both central and tenant schemas
  - AppDbContext compatibility shim for backward compatibility

affects:
  - 01-03 (tenant resolution middleware needs TenantDbContextFactory)
  - 01-04 (tenant provisioning needs CentralDbContext + TenantDbContextFactory)
  - all future plans using CentralDbContext or TenantDbContext

# Tech tracking
tech-stack:
  added:
    - Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1
    - EF Core upgraded to 10.0.5 (all packages aligned)
    - dotnet-ef 10.0.5 (global tool for migrations)
  patterns:
    - Dual-context pattern: CentralDbContext (platform) + TenantDbContext (per-tenant)
    - Global query filter pattern: HasQueryFilter on all BaseTenantEntity types in TenantDbContext
    - Factory pattern: ITenantDbContextFactory creates TenantDbContext from connection string + tenantId
    - AppDbContext compatibility shim: inherits CentralDbContext, registered alongside it in DI
    - DesignTimeDbContextFactory for EF tooling without startup project connection strings

key-files:
  created:
    - IronMonkey.Data/CentralDbContext.cs
    - IronMonkey.Data/TenantDbContext.cs
    - IronMonkey.Data/TenantDbContextFactory.cs
    - IronMonkey.Data/DesignTimeContextFactories.cs
    - IronMonkey.Data/Entities/SignupRequest.cs
    - IronMonkey.Data/Configurations/SignupRequestConfiguration.cs
    - IronMonkey.Data/Migrations/Central/20260320095001_Initial.cs
    - IronMonkey.Data/Migrations/Tenant/20260320095026_Initial.cs
    - IronMonkey.Tests/Integration/TenantIsolationTests.cs
  modified:
    - IronMonkey.Data/AppDbContext.cs (replaced with shim inheriting CentralDbContext)
    - IronMonkey.Data/Extensions/ConfigureDatabase.cs (registers CentralDbContext + factory + shim)
    - IronMonkey.Data/Configurations/TenantConfiguration.cs (added approval/provisioning fields)
    - IronMonkey.Data/Entities/Tenant.cs (added Approve/Reject/MarkProvisioned methods)
    - IronMonkey.Data/IronMonkey.Data.csproj (Npgsql 10.0.1, EF Core 10.0.5, removed SQLite)
    - IronMonkey.ApiService/IronMonkey.ApiService.csproj (Npgsql 10.0.1, EF Core 10.0.5)
    - IronMonkey.AppHost/AppHost.cs (adds Aspire PostgreSQL + CentralDb resource)

key-decisions:
  - "Npgsql 9.0.4 is binary-incompatible with EF Core 10.x — upgraded to Npgsql 10.0.1 for runtime stability"
  - "EF Core upgraded from 10.0.2 to 10.0.5 to match dotnet-ef 10.0.5 tool version requirements"
  - "AppDbContext kept as compatibility shim (extends CentralDbContext) rather than deleted — existing endpoints still compile"
  - "TenantConfiguration uses HasData for Role seeding — tests must attach existing roles, not re-insert"
  - "Tenant entity extends BaseTenantEntity with self-referential TenantId=Id pattern — maintained as-is"

patterns-established:
  - "Dual-context: CentralDbContext for platform-wide data, TenantDbContext for tenant-scoped data"
  - "Tenant isolation via HasQueryFilter in TenantDbContext.OnModelCreating — applied per entity not per query"
  - "TenantDbContext is never registered in DI directly — always created via ITenantDbContextFactory with explicit connStr+tenantId"
  - "Integration tests use TestContainers PostgreSQL with unique DB name per test run for isolation"

requirements-completed:
  - TNCY-01
  - TNCY-02

# Metrics
duration: 28min
completed: 2026-03-20
---

# Phase 01 Plan 02: PostgreSQL Dual-Context Migration Summary

**CentralDbContext + TenantDbContext with EF Core global query filters for TNCY-02 isolation, Npgsql 10.0.1, and passing integration tests via TestContainers**

## Performance

- **Duration:** 28 min
- **Started:** 2026-03-20T09:35:15Z
- **Completed:** 2026-03-20T10:03:35Z
- **Tasks:** 3 (+ TDD RED/GREEN cycle for Task 3)
- **Files modified:** 16

## Accomplishments
- CentralDbContext manages Tenants and SignupRequests against the central PostgreSQL database
- TenantDbContext enforces tenant isolation via global query filters on User, Lead, Contact, Opportunity
- TenantDbContextFactory creates per-tenant DbContext instances from a connection string + tenantId
- EF Core migrations generated for both schemas (Central and Tenant) in separate output directories
- SQLite removed; Npgsql 10.0.1 with EF Core 10.0.5 is the sole database provider
- TenantIsolationTests pass against real PostgreSQL via TestContainers (2/2 green)

## Task Commits

Each task was committed atomically:

1. **Task 1: Add PostgreSQL NuGet packages + Tenant entity extension + SignupRequest entity** - `7b50132` (feat)
2. **Task 2: Create CentralDbContext, TenantDbContext, TenantDbContextFactory, migrations** - `88061b1` (feat)
3. **Task 3: Implement TenantIsolationTests (TDD)** - `cc535aa` (test)

## Files Created/Modified
- `IronMonkey.Data/CentralDbContext.cs` - DbContext for central DB (Tenants, SignupRequests)
- `IronMonkey.Data/TenantDbContext.cs` - DbContext with global query filters for tenant isolation
- `IronMonkey.Data/TenantDbContextFactory.cs` - Factory + interface for creating per-tenant contexts
- `IronMonkey.Data/DesignTimeContextFactories.cs` - EF tooling design-time factories
- `IronMonkey.Data/AppDbContext.cs` - Compatibility shim, now inherits CentralDbContext
- `IronMonkey.Data/Entities/SignupRequest.cs` - New entity for pending tenant signups
- `IronMonkey.Data/Configurations/SignupRequestConfiguration.cs` - EF config for SignupRequest
- `IronMonkey.Data/Extensions/ConfigureDatabase.cs` - Registers CentralDbContext, factory, AppDbContext shim
- `IronMonkey.Data/Entities/Tenant.cs` - Extended with ApprovalStatus, Approve/Reject/MarkProvisioned
- `IronMonkey.Data/Configurations/TenantConfiguration.cs` - Added approval and provisioning columns
- `IronMonkey.Data/Migrations/Central/` - Initial migration for Tenants + SignupRequests
- `IronMonkey.Data/Migrations/Tenant/` - Initial migration for Users, Roles, Leads, Contacts, Opportunities
- `IronMonkey.AppHost/AppHost.cs` - Aspire: adds PostgreSQL + CentralDb resource
- `IronMonkey.Tests/Integration/TenantIsolationTests.cs` - 2 passing integration tests

## Decisions Made
- **Npgsql version**: Used 10.0.1 (stable) rather than 9.x — Npgsql 9.x is binary-incompatible with EF Core 10.x at runtime (method signature mismatch in EF Core internals)
- **EF Core upgrade**: Aligned all EF Core packages to 10.0.5 to match the installed dotnet-ef tool version, eliminating version conflicts
- **AppDbContext shim**: Kept AppDbContext as a compatibility shim rather than deleting it — 10+ existing API endpoints reference it; deletion would break the build before those endpoints are updated
- **Tenant extends BaseTenantEntity**: Pre-existing self-referential pattern (TenantId = own Id) preserved as-is per plan guidance

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Updated ConfigureDatabase.cs before Task 1 build verification**
- **Found during:** Task 1 (Add NuGet packages)
- **Issue:** Removing SQLite package caused `UseSqlite()` call in ConfigureDatabase.cs to fail compilation
- **Fix:** Updated ConfigureDatabase.cs to use `UseNpgsql()` (temporary, then fully replaced in Task 2)
- **Files modified:** `IronMonkey.Data/Extensions/ConfigureDatabase.cs`
- **Committed in:** `7b50132` (part of Task 1 commit)

**2. [Rule 3 - Blocking] Upgraded Npgsql from 9.0.4 to 10.0.1**
- **Found during:** Task 2 (Generate EF migrations)
- **Issue:** Npgsql 9.0.4 is binary-incompatible with EF Core 10.x — `Method not found: ArgumentIsEmpty` at migration generation runtime. Plan specified version 10.0.2 which does not exist; only 9.x and 10.0.x+ are available.
- **Fix:** Upgraded to Npgsql 10.0.1 (stable, compatible with EF Core 10.x). Also upgraded all EF Core packages to 10.0.5 to match dotnet-ef tool version.
- **Files modified:** `IronMonkey.Data/IronMonkey.Data.csproj`, `IronMonkey.ApiService/IronMonkey.ApiService.csproj`
- **Committed in:** `88061b1` (part of Task 2 commit)

**3. [Rule 1 - Bug] Fixed test role seeding approach**
- **Found during:** Task 3 (TDD integration tests)
- **Issue:** Tests tried to manually insert Role entities, but RoleConfiguration uses HasData to seed roles. EnsureCreatedAsync applies the seed, so re-inserting Role with the same PK caused duplicate key violation.
- **Fix:** Changed tests to use `dbContext.Roles.Find(Role.TeleCaller.Id)` to attach the existing seeded role instead of creating a new one.
- **Files modified:** `IronMonkey.Tests/Integration/TenantIsolationTests.cs`
- **Committed in:** `cc535aa` (Task 3 commit)

---

**Total deviations:** 3 auto-fixed (2 blocking, 1 bug)
**Impact on plan:** All auto-fixes necessary for correctness and project buildability. Npgsql version upgrade is a straightforward dependency resolution. No scope creep.

## Issues Encountered
- Npgsql version planning mismatch: plan specified version 10.0.2 which does not exist on NuGet (only 9.x and 10.0.x). The 9.x branch uses EF Core 9 APIs incompatible with EF Core 10 at runtime. Resolved by using Npgsql 10.0.1.

## Next Phase Readiness
- CentralDbContext and TenantDbContextFactory are ready for use by Plan 03 (TenantResolutionMiddleware)
- Plan 03 needs TenantId from JWT claims to call `TenantDbContextFactory.CreateForTenant()`
- `AppDbContext` shim will need gradual migration — each endpoint should be updated to use `CentralDbContext` or `TenantDbContext` explicitly in later plans

---
*Phase: 01-multi-tenancy-foundation*
*Completed: 2026-03-20*

## Self-Check: PASSED

All files created, all commits verified.
