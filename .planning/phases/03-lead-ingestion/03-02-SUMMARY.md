---
phase: 03-lead-ingestion
plan: 02
subsystem: database
tags: [efcore, postgresql, migrations, entities, csvhelper, multi-tenant]

# Dependency graph
requires:
  - phase: 02-configurable-lead-model
    provides: Lead entity, BaseTenantEntity base class, EF Core migration infrastructure
  - phase: 03-01
    provides: Wave 0 test stubs for ingestion channels

provides:
  - ApiKey entity in CentralDbContext with BCrypt hash storage and key prefix
  - WebForm entity in CentralDbContext with form token and JSON field configuration
  - ImportBatch entity in TenantDbContext with status tracking (Pending/Processing/Complete/Failed)
  - IsPotentialDuplicate and PotentialDuplicateLeadId fields on Lead entity
  - EF migrations for both central and tenant databases (AddIngestionEntities)
  - CsvHelper 33.1.0 package in ApiService project

affects:
  - 03-03 (API ingestion — depends on ApiKey entity)
  - 03-04 (CSV import — depends on ImportBatch entity and CsvHelper)
  - 03-05 (web form — depends on WebForm entity)

# Tech tracking
tech-stack:
  added:
    - CsvHelper 33.1.0 (ApiService project)
  patterns:
    - Entity factory method pattern: static Create(...) constructors on all new entities
    - BCrypt hash storage: ApiKey.KeyHash stores hash, KeyPrefix stores first 8 chars for identification
    - JSON field config: WebForm.FieldNamesJson stores field list as JSON array, accessed via GetFieldNames()
    - ImportBatch progress tracking: MarkStarted/UpdateProgress/MarkComplete/MarkFailed lifecycle methods
    - TenantId query filter on ImportBatch: enforces tenant isolation in TenantDbContext

key-files:
  created:
    - IronMonkey.Data/Entities/ApiKey.cs
    - IronMonkey.Data/Entities/WebForm.cs
    - IronMonkey.Data/Entities/ImportBatch.cs
    - IronMonkey.Data/Migrations/Central/20260321182206_AddIngestionEntities.cs
    - IronMonkey.Data/Migrations/Tenant/20260321182230_AddIngestionEntities.cs
  modified:
    - IronMonkey.Data/Entities/Lead.cs
    - IronMonkey.Data/CentralDbContext.cs
    - IronMonkey.Data/TenantDbContext.cs
    - IronMonkey.ApiService/IronMonkey.ApiService.csproj

key-decisions:
  - "ApiKey in CentralDbContext (not TenantDbContext) — enables O(1) lookup by key hash without knowing tenant upfront"
  - "WebForm in CentralDbContext (not TenantDbContext) — form token lookup must work before tenant context resolved"
  - "ImportBatch in TenantDbContext — import state belongs to tenant; query filter enforces isolation"
  - "FieldNamesJson as JSON string on WebForm — flexible field config without extra join table"
  - "KeyPrefix stores first 8 chars of plaintext key — safe identification in logs/UI without exposing full key"

patterns-established:
  - "ImportBatch lifecycle: Pending -> Processing -> Complete/Failed via dedicated methods"
  - "JSON config in SQL: string columns storing JSON for flexible config (FieldNamesJson)"

requirements-completed: [INGST-01, INGST-02, INGST-03, INGST-04]

# Metrics
duration: 6min
completed: 2026-03-21
---

# Phase 3 Plan 02: Ingestion Entities and EF Migrations Summary

**ApiKey, WebForm, and ImportBatch entities with EF Core migrations for both central and tenant databases, plus CsvHelper package and Lead duplicate-detection fields**

## Performance

- **Duration:** ~6 min
- **Started:** 2026-03-21T18:19:36Z
- **Completed:** 2026-03-21T18:25:16Z
- **Tasks:** 2
- **Files modified:** 9

## Accomplishments
- Created ApiKey entity (central DB) with BCrypt hash storage and key prefix for safe identification
- Created WebForm entity (central DB) with unique form token, JSON field config, and redirect URL support
- Created ImportBatch entity (tenant DB) with full status lifecycle (Pending/Processing/Complete/Failed) and row count tracking
- Added IsPotentialDuplicate + PotentialDuplicateLeadId + MarkAsPotentialDuplicate() to Lead entity
- Registered all entities in their respective DbContexts with proper query filters and index configurations
- Generated AddIngestionEntities EF migration for both CentralDbContext and TenantDbContext
- Installed CsvHelper 33.1.0 in ApiService project (required for plans 03-04)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create ApiKey, WebForm, ImportBatch entities and install CsvHelper** - `56b4a6a` (feat)
2. **Task 2: Register entities in DbContexts and run EF migrations** - `25d1f05` (feat)

**Plan metadata:** (docs commit — see final_commit below)

## Files Created/Modified
- `IronMonkey.Data/Entities/ApiKey.cs` - Tenant API key entity with BCrypt hash storage, TenantId for central DB lookup
- `IronMonkey.Data/Entities/WebForm.cs` - Web form entity with unique form token, JSON field config, optional redirect URL
- `IronMonkey.Data/Entities/ImportBatch.cs` - CSV import batch entity with status lifecycle and row count tracking
- `IronMonkey.Data/Entities/Lead.cs` - Added IsPotentialDuplicate, PotentialDuplicateLeadId, MarkAsPotentialDuplicate()
- `IronMonkey.Data/CentralDbContext.cs` - Added DbSet<ApiKey>, DbSet<WebForm> with table/index configuration
- `IronMonkey.Data/TenantDbContext.cs` - Added DbSet<ImportBatch> with TenantId query filter
- `IronMonkey.ApiService/IronMonkey.ApiService.csproj` - Added CsvHelper 33.1.0 package reference
- `IronMonkey.Data/Migrations/Central/20260321182206_AddIngestionEntities.cs` - Adds ApiKeys and WebForms tables
- `IronMonkey.Data/Migrations/Tenant/20260321182230_AddIngestionEntities.cs` - Adds ImportBatches table and Lead duplicate columns

## Decisions Made
- ApiKey placed in CentralDbContext rather than TenantDbContext — API key verification must resolve tenant from key hash without prior tenant context
- WebForm placed in CentralDbContext — form token lookup must work before tenant is resolved from URL
- WebForm.FieldNamesJson stores field list as JSON string — avoids extra join table while keeping flexibility
- ApiKey.KeyPrefix stores first 8 chars only — safe for logs and UI display without exposing the full key
- ImportBatch query filter uses only TenantId (not IsDeleted) — import records are audit trail and should not be soft-deleted

## Deviations from Plan

None — plan executed exactly as written.

## Issues Encountered
- Git stash incident during verification: ran `git stash` to test pre-existing Web error, stash pop failed on obj file conflicts. Manually re-applied CentralDbContext and TenantDbContext changes. Migration files were untracked and survived intact. No data loss.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness
- ApiKey entity ready for 03-03 (API ingestion endpoint and key management)
- ImportBatch entity and CsvHelper ready for 03-04 (CSV import endpoint and Hangfire job)
- WebForm entity ready for 03-05 (web form management and submission endpoint)
- Lead.MarkAsPotentialDuplicate() ready for use in import and API ingestion flows
- Note: pre-existing CS0542 error in IronMonkey.Web (CreatePermission Razor component naming conflict) exists before this plan and is out of scope

---
*Phase: 03-lead-ingestion*
*Completed: 2026-03-21*

## Self-Check: PASSED

- FOUND: IronMonkey.Data/Entities/ApiKey.cs
- FOUND: IronMonkey.Data/Entities/WebForm.cs
- FOUND: IronMonkey.Data/Entities/ImportBatch.cs
- FOUND: Central migration (20260321182206_AddIngestionEntities.cs)
- FOUND: Tenant migration (20260321182230_AddIngestionEntities.cs)
- FOUND: commit 56b4a6a (Task 1)
- FOUND: commit 25d1f05 (Task 2)
