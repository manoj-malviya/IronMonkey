---
phase: 03-lead-ingestion
plan: 07
subsystem: testing
tags: [xunit, testcontainers, postgres, bcrypt, csvhelper, integration-tests, unit-tests]

# Dependency graph
requires:
  - phase: 03-lead-ingestion
    provides: All four ingestion channel implementations (manual, CSV, API key, web form)
provides:
  - "13 passing unit tests covering CSV validation, API key BCrypt hashing, and honeypot detection"
  - "21 passing integration tests covering ManualLeadCreation, CsvImport, ApiLeadCreation, and WebForm channels"
  - "Human-verified: hosted web form page renders correctly with hidden honeypot field"
  - "Full test suite green (61 tests, 0 failures)"
affects: [future-phases]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Unique DB per integration test via GUID suffix in connection string"
    - "Direct service instantiation in integration tests (no HTTP layer, no DI container)"
    - "Moq ITenantService stub pattern for service-level integration tests"
    - "Inline PostgreSQL model snapshot sync to resolve EF Core migration drift"

key-files:
  created: []
  modified:
    - IronMonkey.Tests/Unit/CsvValidationTests.cs
    - IronMonkey.Tests/Unit/ApiAuthTests.cs
    - IronMonkey.Tests/Unit/HoneypotTests.cs
    - IronMonkey.Tests/Integration/ManualLeadCreationTests.cs
    - IronMonkey.Tests/Integration/CsvImportTests.cs
    - IronMonkey.Tests/Integration/ApiLeadCreationTests.cs
    - IronMonkey.Tests/Integration/WebFormTests.cs
    - IronMonkey.Data/Migrations/Central/CentralDbContextModelSnapshot.cs
    - IronMonkey.Data/Migrations/Tenant/TenantDbContextModelSnapshot.cs

key-decisions:
  - "EF Core model snapshot drift fixed inline during test implementation — migration snapshot must match all registered entities or MigrateAsync() fails in TestContainers"
  - "Integration tests invoke services directly (not via HTTP) — eliminates WebApplicationFactory complexity while still testing against real PostgreSQL"

patterns-established:
  - "Unit test isolation: CsvValidationTests instantiates CsvImportService directly with no DB; ApiAuthTests exercises BCrypt in isolation with workFactor=4 for speed"
  - "Integration test provisioning: each test creates a unique GUID-suffixed tenant DB, calls MigrateAsync(), inserts a PipelineStage seed, then runs the service under test"

requirements-completed: [INGST-01, INGST-02, INGST-03, INGST-04]

# Metrics
duration: 45min
completed: 2026-03-21
---

# Phase 3 Plan 07: Lead Ingestion Tests Summary

**34 xUnit tests (13 unit + 21 integration) proving all four Phase 3 ingestion channels work end-to-end against real PostgreSQL via TestContainers**

## Performance

- **Duration:** ~45 min
- **Started:** 2026-03-21T18:47:43Z
- **Completed:** 2026-03-21T19:01:24Z
- **Tasks:** 3 (2 auto + 1 checkpoint, checkpoint human-approved)
- **Files modified:** 9

## Accomplishments
- Replaced all 34 `[Fact(Skip)]` stubs with fully passing tests — no skips remain
- Integration tests validate: duplicate detection in manual creation, CSV row skipping with error JSON, BCrypt API key verify/delete lifecycle, web form token lookup and honeypot rejection
- Full solution test suite green: 61 tests, 0 failures, 0 skipped

## Task Commits

Each task was committed atomically:

1. **Task 1: Implement unit tests (CsvValidationTests, ApiAuthTests, HoneypotTests)** - `bc87a1e` (feat)
2. **Task 2: Implement integration tests (ManualLeadCreation, CsvImport, ApiLeadCreation, WebForm)** - `dbf368d` (feat)
3. **Task 3: Human verification checkpoint** - approved by user

## Files Created/Modified
- `IronMonkey.Tests/Unit/CsvValidationTests.cs` - 6 tests: header validation (valid, missing column, case mismatch), row parsing (success, missing FirstName, missing Email)
- `IronMonkey.Tests/Unit/ApiAuthTests.cs` - 4 tests: key generation format, BCrypt verify correct/wrong/empty key
- `IronMonkey.Tests/Unit/HoneypotTests.cs` - 3 tests: null/empty returns false, filled value returns true, whitespace returns true
- `IronMonkey.Tests/Integration/ManualLeadCreationTests.cs` - 4 tests: no-duplicate create, duplicate warning, force create, custom fields storage
- `IronMonkey.Tests/Integration/CsvImportTests.cs` - 5 tests: batch creation, all valid rows imported, bad rows skipped, duplicate flagged, batch status counts
- `IronMonkey.Tests/Integration/ApiLeadCreationTests.cs` - 6 tests: key generation, key delete/invalidate, validate correct key, empty key, wrong key, duplicate detection
- `IronMonkey.Tests/Integration/WebFormTests.cs` - 6 tests: form creation with token, lead creation via WebForm source, honeypot rejection, invalid token returns null, duplicate flagged, form fields available for rendering
- `IronMonkey.Data/Migrations/Central/CentralDbContextModelSnapshot.cs` - Synced snapshot to include ApiKey entity
- `IronMonkey.Data/Migrations/Tenant/TenantDbContextModelSnapshot.cs` - Synced snapshot to include ImportBatch and WebForm entities

## Decisions Made
- Kept integration tests at service layer rather than HTTP layer — avoids WebApplicationFactory complexity while still proving real DB correctness
- Fixed EF Core model snapshot drift as part of Task 2 (Rule 3 auto-fix) — MigrateAsync() in TestContainers requires snapshot to match all registered entities

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed EF Core model snapshot sync causing duplicate migration generation**
- **Found during:** Task 2 (CsvImportTests provisioning)
- **Issue:** CentralDbContextModelSnapshot and TenantDbContextModelSnapshot were out of sync with entities added in Phase 3 (ApiKey, ImportBatch, WebForm). `MigrateAsync()` in TestContainers attempted to generate duplicate migrations, failing test setup.
- **Fix:** Updated both snapshot files inline to include the missing entity configurations, aligning snapshots with existing migration history
- **Files modified:** `IronMonkey.Data/Migrations/Central/CentralDbContextModelSnapshot.cs`, `IronMonkey.Data/Migrations/Tenant/TenantDbContextModelSnapshot.cs`
- **Verification:** Full test suite passes (61 tests, 0 failures)
- **Committed in:** `dbf368d` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Auto-fix essential for test provisioning correctness. No scope creep.

## Issues Encountered
- EF Core snapshot drift: Phase 3 entities (ApiKey, WebForm, ImportBatch) were added via migrations but model snapshots were not updated, causing TestContainers MigrateAsync() to fail. Fixed by syncing snapshots to match existing migration history.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All four Phase 3 ingestion channels are implemented and test-verified
- Phase 3 (03-lead-ingestion) is complete — all 7 plans executed
- Ready to begin Phase 4 when planned

---
*Phase: 03-lead-ingestion*
*Completed: 2026-03-21*
