---
phase: 01-multi-tenancy-foundation
plan: 06
subsystem: testing
tags: [xunit, testcontainers, postgres, ef-core, migrations, integration-tests, unit-tests]

# Dependency graph
requires:
  - phase: 01-multi-tenancy-foundation
    provides: "All Plans 01-05: test scaffold, dual-context DB, JWT pipeline, provisioning service, Hangfire jobs"
provides:
  - "10/10 tests passing — phase gate satisfied, zero skipped, zero failures"
  - "TenantIsolationTests: EF global query filter isolation verified across two separate DBs"
  - "CrossTenantSecurityTests: JWT tenant_id claim enforcement verified at service layer"
  - "TenantProvisioningTests: full signup → approve → provision flow verified end-to-end"
  - "HangfireJobTenantTests: explicit TenantId parameter verified in Hangfire job execution"
  - "AddOutboxMessagePublished migration: TenantDbContext schema aligned with OutboxMessage.Published property"
affects: [phase-02-lead-management, phase-03-pipeline]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Missing EF Core migration for model changes surfaces as PendingModelChangesWarning at test runtime — always add migration after adding mapped properties"

key-files:
  created:
    - IronMonkey.Data/Migrations/Tenant/20260320105659_AddOutboxMessagePublished.cs
    - IronMonkey.Data/Migrations/Tenant/20260320105659_AddOutboxMessagePublished.Designer.cs
  modified:
    - IronMonkey.Data/Migrations/Tenant/TenantDbContextModelSnapshot.cs

key-decisions:
  - "OutboxMessage.Published migration added inline rather than via EnsureCreated — MigrateAsync() used in TenantProvisioningService requires full migration history alignment"

patterns-established:
  - "Phase gate pattern: all Phase 1 requirements verified by automated tests before advancing"

requirements-completed: [TNCY-01, TNCY-02]

# Metrics
duration: 15min
completed: 2026-03-20
---

# Phase 1 Plan 06: Test Suite Completion Summary

**All 10 Phase 1 integration and unit tests passing: EF migration gap for OutboxMessage.Published fixed, phase gate green with zero failures and zero skipped tests.**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-03-20T10:56:00Z
- **Completed:** 2026-03-20T11:11:00Z
- **Tasks:** 1 (Task 2 is a checkpoint:human-verify)
- **Files modified:** 3

## Accomplishments
- Discovered 2 failing tests caused by `PendingModelChangesWarning` — EF Core's `MigrateAsync` blocked on model/migration mismatch
- Added `AddOutboxMessagePublished` migration to align TenantDbContext schema with `OutboxMessage.Published` property (added in Plan 05)
- Achieved 10/10 tests passing: TenantIsolationTests (2), CrossTenantSecurityTests (2), TenantProvisioningTests (4), HangfireJobTenantTests (2)

## Task Commits

Each task was committed atomically:

1. **Task 1: Fix PendingModelChangesWarning + achieve green test suite** - `3751aa8` (fix)

2. **Checkpoint fix: prevent Role re-insertion in TenantIsolationTests** - `0de021c` (fix)

**Plan metadata:** (docs commit after checkpoint)

## Files Created/Modified
- `IronMonkey.Data/Migrations/Tenant/20260320105659_AddOutboxMessagePublished.cs` - EF migration adding Published boolean column (default false) to outbox_messages
- `IronMonkey.Data/Migrations/Tenant/20260320105659_AddOutboxMessagePublished.Designer.cs` - Auto-generated migration designer
- `IronMonkey.Data/Migrations/Tenant/TenantDbContextModelSnapshot.cs` - Updated snapshot including Published property

## Decisions Made
- Used `dotnet ef migrations add` to create a proper migration rather than switching to `EnsureCreated()` — `TenantProvisioningService` calls `MigrateAsync()` in production path, which requires valid migration history

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Added missing TenantDbContext migration for OutboxMessage.Published**
- **Found during:** Task 1 (run test suite — 2 tests failing)
- **Issue:** `OutboxMessage.Published` property was added in Plan 05 with `OutboxMessageConfiguration` (HasDefaultValue false) but no corresponding EF migration was created. `MigrateAsync()` threw `PendingModelChangesWarning` as an exception at runtime.
- **Fix:** Ran `dotnet ef migrations add AddOutboxMessagePublished --context TenantDbContext` — generated migration adds `Published boolean NOT NULL DEFAULT false` to `outbox_messages` table
- **Files modified:** IronMonkey.Data/Migrations/Tenant/20260320105659_AddOutboxMessagePublished.cs, .Designer.cs, TenantDbContextModelSnapshot.cs
- **Verification:** `dotnet test` reports 10 passed, 0 failed, 0 skipped
- **Committed in:** 3751aa8 (Task 1 commit)

---

**2. [Rule 1 - Bug] Fixed Role re-insertion causing unique constraint violation in TenantIsolationTests**
- **Found during:** Task 2 (human-verify checkpoint — 2 tests failing)
- **Issue:** TenantIsolationTests.SeedAsync called `db.Roles.Add(new Role("Admin"))` unconditionally after `MigrateAsync()` had already seeded the Admin role. On the second test database creation the unique constraint fired.
- **Fix:** Changed seed to load-or-insert: `await db.Roles.FirstOrDefaultAsync(r => r.Name == "Admin") ?? new Role("Admin")`
- **Files modified:** IronMonkey.Tests/Integration/TenantIsolationTests.cs
- **Verification:** All 10 tests pass with 0 failed, 0 skipped
- **Committed in:** 0de021c (post-checkpoint fix)

---

**Total deviations:** 2 auto-fixed (2x Rule 1 - runtime bugs)
**Impact on plan:** Both auto-fixes required for test idempotency and correctness. No scope creep.

## Issues Encountered
- Tests were written in Plans 02-05 but not yet run end-to-end. The `Published` property gap was invisible until `TenantProvisioningService.ProvisionTenantAsync()` ran `MigrateAsync()` in a real test container.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Phase 1 multi-tenancy foundation is complete and verified
- All TNCY-01 and TNCY-02 requirements satisfied by automated tests
- PostgreSQL DB-per-tenant provisioning, JWT tenant claim enforcement, EF global query filters, and Hangfire TenantId isolation are all production-ready
- Ready for Phase 2: Lead Management

---
*Phase: 01-multi-tenancy-foundation*
*Completed: 2026-03-20*
