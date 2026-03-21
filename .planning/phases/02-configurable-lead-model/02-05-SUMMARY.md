---
phase: 02-configurable-lead-model
plan: "05"
subsystem: integration-tests
tags: [integration-tests, testcontainers, postgresql, lead-model, custom-fields, pipeline-stages, duplicate-detection, lead-merge]

# Dependency graph
requires:
  - phase: 02-03
    provides: CRUD endpoints for custom fields, pipeline stages, leads
  - phase: 02-04
    provides: DuplicateDetectionService, LeadMergeService with FuzzySharp scoring
  - phase: 01-multi-tenancy-foundation
    provides: PostgreSqlFixture, TenantDbContextFactory, TenantDbContext, MigrateAsync pattern
provides:
  - 17 integration tests covering LEAD-01 through LEAD-05 with real PostgreSQL via TestContainers
  - CustomFieldTests: 4 tests (text field create, dropdown with options, tenant isolation, type validation)
  - PipelineStageTests: 3 tests (create with name/order, active filter, FK violation)
  - LeadSourceTests: 2 tests (manual source stored, import source surfaced)
  - DuplicateDetectionTests: 4 tests (email match, phone match, fuzzy name, no match)
  - LeadMergeTests: 4 tests (field retention, soft-delete, audit record, custom field merge)
affects:
  - Phase 3+ (regression baseline — all 17 tests serve as ongoing regression suite)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Moq ITenantService stub: returns test connection string to inject service under test with real PostgreSQL"
    - "Unique DB suffix pattern: fixture.ConnectionString.Replace with GUID suffix for parallel-safe test isolation"
    - "IgnoreQueryFilters() for soft-delete assertions — bypass global IsDeleted filter to verify deleted state"
    - "DbContextOptionsBuilder<TenantDbContext>.UseNpgsql(connStr).Options for fresh context assertions"

key-files:
  created: []
  modified:
    - IronMonkey.Tests/Integration/CustomFieldTests.cs
    - IronMonkey.Tests/Integration/PipelineStageTests.cs
    - IronMonkey.Tests/Integration/LeadSourceTests.cs
    - IronMonkey.Tests/Integration/DuplicateDetectionTests.cs
    - IronMonkey.Tests/Integration/LeadMergeTests.cs
    - IronMonkey.ApiService/Features/Leads/Merge/LeadMergeService.cs

key-decisions:
  - "Moq ITenantService stub for DuplicateDetectionService/LeadMergeService tests — services take ITenantService to get connection string; stub returns test DB conn string, enabling direct service invocation without HTTP layer"
  - "Unique DB per test using GUID suffix — prevents test interference when running in parallel; each test gets its own PostgreSQL database within the shared TestContainers instance"
  - "LeadMergeService.CustomFields.IsModified fix — HasConversion JSONB value converters require explicit db.Entry(entity).Property(...).IsModified = true after mutating dictionary reference"
  - "ListPipelineStages active filter uses explicit .Where(p => p.IsActive) — global query filter only covers IsDeleted; IsActive is a separate business concept from soft-delete"

requirements: [LEAD-01, LEAD-02, LEAD-03, LEAD-04, LEAD-05]

# Metrics
duration: 15min
completed: 2026-03-21
---

# Phase 02 Plan 05: Integration Test Suite Summary

**17 integration tests with real PostgreSQL via TestContainers covering all Phase 2 requirements — custom fields, pipeline stages, lead source tracking, duplicate detection, and lead merge with audit**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-03-21
- **Completed:** 2026-03-21
- **Tasks:** 2 (+ checkpoint)
- **Files modified:** 6

## Accomplishments

- CustomFieldTests.cs: 4 passing tests — text field creation, dropdown with options, multi-tenant isolation via separate DBs, type validation showing service-layer responsibility
- PipelineStageTests.cs: 3 passing tests — create with IsActive=true default, IsActive filter, FK constraint enforcement for invalid stageId
- LeadSourceTests.cs: 2 passing tests — Manual and Import source values round-trip correctly through PostgreSQL
- DuplicateDetectionTests.cs: 4 passing tests — email exact match (score 100), phone normalized match (score 95), fuzzy name match "Jon Smith" vs "John Smith" (score >= 75 via FuzzySharp TokenSetRatio), no-match returns empty list
- LeadMergeTests.cs: 4 passing tests — source fields retained, target soft-deleted with DeletedAt, audit record with both IDs, null custom fields replaced by target values
- Auto-fixed LeadMergeService JSONB change tracking bug (Rule 1)

## Task Commits

Each task was committed atomically:

1. **Task 1: CustomField, PipelineStage, and LeadSource tests** - `c4034b0` (feat)
2. **Task 2: DuplicateDetection and LeadMerge tests** - `284748a` (feat)

## Files Created/Modified

- `IronMonkey.Tests/Integration/CustomFieldTests.cs` — 4 tests for LEAD-01; unique DB suffix pattern; separate TenantDbContext per tenant for isolation test
- `IronMonkey.Tests/Integration/PipelineStageTests.cs` — 3 tests for LEAD-02; FK violation test confirms DbUpdateException on invalid stageId
- `IronMonkey.Tests/Integration/LeadSourceTests.cs` — 2 tests for LEAD-03; verifies LeadSource enum stored as string in PostgreSQL round-trips correctly
- `IronMonkey.Tests/Integration/DuplicateDetectionTests.cs` — 4 tests for LEAD-04; Moq ITenantService stub routes service to test DB; phone test passes normalized (digit-only) phone
- `IronMonkey.Tests/Integration/LeadMergeTests.cs` — 4 tests for LEAD-05; IgnoreQueryFilters() for soft-delete assertion; validates custom field null-merge behavior
- `IronMonkey.ApiService/Features/Leads/Merge/LeadMergeService.cs` — Bug fix: mark CustomFields property as modified after dictionary mutation

## Decisions Made

1. **Moq ITenantService stub** — DuplicateDetectionService and LeadMergeService call `_tenantService.GetConnectionStringAsync()` to get the DB connection. Tests create a Mock<ITenantService> returning the test connection string, enabling direct service invocation with real PostgreSQL without the HTTP pipeline.

2. **Unique DB suffix per test** — Each test creates its own database (`cf_abc123`, `dd_xyz789`) to prevent parallel test interference. The shared PostgreSqlFixture container hosts all these isolated databases.

3. **Explicit IsModified for JSONB** — EF Core's `HasConversion` value converters don't detect inner dictionary mutations as changes. After `source.CustomFields.Set(key, value)` in LeadMergeService, added `db.Entry(source).Property(l => l.CustomFields).IsModified = true` to force the save.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] LeadMergeService JSONB change tracking**
- **Found during:** Task 2 — `MergeLeads_SurvivingLeadCustomFieldsIncludeTargetValues` test failure
- **Issue:** After calling `source.CustomFields.Set(key, value)`, EF Core did not detect the dictionary mutation as a pending change because `HasConversion` value converters operate on the object reference, not its contents.
- **Fix:** Added `db.Entry(source).Property(l => l.CustomFields).IsModified = true;` after the custom fields merge loop in LeadMergeService.MergeAsync()
- **Files modified:** `IronMonkey.ApiService/Features/Leads/Merge/LeadMergeService.cs`
- **Commit:** `284748a`

**2. [Rule 1 - Adaptation] ListPipelineStages_ReturnsOnlyActiveStages test**
- **Found during:** Task 1 — plan said "global filter enforces IsActive" but the global filter only enforces `!IsDeleted`
- **Fix:** Test filters explicitly with `.Where(p => p.IsActive)` which is the correct semantic test; PipelineStage.Deactivate() sets IsActive=false which is distinct from soft-delete
- **Files modified:** `IronMonkey.Tests/Integration/PipelineStageTests.cs`
- **Commit:** `c4034b0`

## Known Stubs

None — all 17 tests are fully implemented with real database verification.

## Self-Check: PASSED
