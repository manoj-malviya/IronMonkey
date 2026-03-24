---
phase: 03-lead-ingestion
plan: 01
subsystem: testing
tags: [xunit, testcontainers, stubs, lead-ingestion, tdd]

# Dependency graph
requires:
  - phase: 02-configurable-lead-model
    provides: Lead entity, duplicate detection, custom fields (stubs reference these behaviors)
provides:
  - Wave 0 test scaffolding for all four INGST requirements (INGST-01 through INGST-04)
  - 7 stub test files with 34 named [Fact(Skip)] tests defining the behavioral contract
affects: [03-02, 03-03, 03-04, 03-05, 03-06]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "[Fact(Skip)] stubs as test-first contract — stub names define behavior before any implementation"
    - "Integration tests: [Collection(\"Integration\")] + IClassFixture<PostgreSqlFixture>"
    - "Unit tests: plain public class, no fixture"

key-files:
  created:
    - IronMonkey.Tests/Integration/ManualLeadCreationTests.cs
    - IronMonkey.Tests/Integration/CsvImportTests.cs
    - IronMonkey.Tests/Integration/ApiLeadCreationTests.cs
    - IronMonkey.Tests/Integration/WebFormTests.cs
    - IronMonkey.Tests/Unit/CsvValidationTests.cs
    - IronMonkey.Tests/Unit/ApiAuthTests.cs
    - IronMonkey.Tests/Unit/HoneypotTests.cs
  modified: []

key-decisions:
  - "03-06-PLAN chosen as implementation target for all Phase 3 stubs — consistent with Phase 1 and Phase 2 naming convention"

patterns-established:
  - "Wave 0 pattern: all stub skip messages reference the implementation plan (03-06-PLAN)"
  - "Integration stubs use IClassFixture<PostgreSqlFixture> even without active fixture use — ready for implementation"

requirements-completed: [INGST-01, INGST-02, INGST-03, INGST-04]

# Metrics
duration: 4min
completed: 2026-03-21
---

# Phase 3 Plan 01: Lead Ingestion Wave 0 Test Scaffolding Summary

**Seven [Fact(Skip)] stub files establishing the behavioral test contract for all four lead ingestion channels — manual entry, CSV import, API key, and web form — before any implementation begins.**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-21T18:19:31Z
- **Completed:** 2026-03-21T18:23:45Z
- **Tasks:** 2
- **Files modified:** 7

## Accomplishments

- Created 4 integration test stub files (21 skipped tests) covering INGST-01 through INGST-04
- Created 3 unit test stub files (13 skipped tests) covering CSV validation, API key auth, and honeypot logic
- All 34 stubs compile and exit with code 0 (skipped, not failed)

## Task Commits

Each task was committed atomically:

1. **Task 1: Integration test stubs (ManualLeadCreationTests, CsvImportTests, ApiLeadCreationTests, WebFormTests)** - `bcb60db` (test)
2. **Task 2: Unit test stubs (CsvValidationTests, ApiAuthTests, HoneypotTests)** - `c3ce747` (test)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `IronMonkey.Tests/Integration/ManualLeadCreationTests.cs` - 4 stubs for INGST-01 (manual entry, duplicate warning, force create, custom fields)
- `IronMonkey.Tests/Integration/CsvImportTests.cs` - 5 stubs for INGST-02 (upload, process, bad rows, duplicate flagging, status)
- `IronMonkey.Tests/Integration/ApiLeadCreationTests.cs` - 6 stubs for INGST-03 (API key auth, missing/invalid key, duplicates in response, key management)
- `IronMonkey.Tests/Integration/WebFormTests.cs` - 6 stubs for INGST-04 (form creation, honeypot, token validation, duplicate flagging, HTML rendering)
- `IronMonkey.Tests/Unit/CsvValidationTests.cs` - 6 stubs for INGST-02 (row parsing, required fields, header validation, case sensitivity)
- `IronMonkey.Tests/Unit/ApiAuthTests.cs` - 4 stubs for INGST-03 (key generation, verify correct/wrong/empty key)
- `IronMonkey.Tests/Unit/HoneypotTests.cs` - 3 stubs for INGST-04 (empty field = not bot, filled = bot, whitespace = bot)

## Decisions Made

- 03-06-PLAN chosen as the implementation target for all Phase 3 stubs — matches the pattern from Phase 1 and Phase 2 (where the final plan in each phase implements the stubs from the Wave 0 plan).

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- NuGet assets missing on first build attempt — resolved by running `dotnet restore` before building. Pre-existing environment state, not caused by this plan.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Wave 0 complete: all 7 stub files exist with correct class names matching VALIDATION.md filter commands
- 34 tests total: 21 integration + 13 unit, all skipped (exit 0)
- Stub names define the behavioral contract for later implementation waves (03-02 through 03-06)
- No blockers for proceeding to Wave 1 (03-02 onwards)

---
*Phase: 03-lead-ingestion*
*Completed: 2026-03-21*
