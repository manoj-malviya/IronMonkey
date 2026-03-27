---
phase: 08-onboarding-admin-api
plan: "03"
subsystem: integration-tests
tags: [testing, recipes, integration, onboarding, admin-api]
dependency_graph:
  requires: [08-01, 08-02]
  provides: [RecipeEndpointTests]
  affects: [IronMonkey.Tests]
tech_stack:
  added: []
  patterns: [xUnit, PostgreSqlFixture, IClassFixture, integration-tests-via-service-layer]
key_files:
  created:
    - IronMonkey.Tests/Integration/RecipeEndpointTests.cs
  modified: []
decisions:
  - "RecipeEndpointTests tests via DB/service layer directly (not HTTP client) — consistent with all other integration tests in this codebase"
  - "Tenant lookup in provisioning tests uses unique company name (not AdminEmail — Tenant entity has no AdminEmail field)"
  - "UniqueSlug helper enforces max 40-char slugs to avoid DB constraint violations"
metrics:
  duration_minutes: 15
  completed_date: "2026-03-26"
  tasks_completed: 1
  files_created: 1
  files_modified: 0
---

# Phase 8 Plan 03: RecipeEndpointTests Summary

14 integration tests for all Phase 8 requirements: recipe CRUD behavior, deactivated-recipe provisioning guard, signup RecipeId flow, and RecipeContentValidator validation rules.

## What Was Built

`IronMonkey.Tests/Integration/RecipeEndpointTests.cs` — a single test class with 14 `[Fact]` tests covering all Phase 8 requirements (ONBD-01, ONBD-02, ONBD-03, ONBD-05, RADM-01, RADM-02, RADM-03, D-14).

Tests are structured in the same pattern as `RecipeProvisioningTests.cs` — calling the DB/entity/service layer directly rather than through a WebApplicationFactory HTTP client.

## Test Coverage by Requirement

| Requirement | Tests |
|-------------|-------|
| ONBD-01 | Test 10 (signup with RecipeId → provisioning), Test 11 (null RecipeId defaults to Blank) |
| ONBD-02 | Test 2 (preview active recipe), Test 3 (preview deactivated → null) |
| ONBD-03 | Test 9 (deactivated recipe guard), Test 10 (recipe applied at provisioning) |
| ONBD-05 | Test 1 (list returns only active) |
| RADM-01 | Test 4 (create persists to DB), Test 5 (duplicate slug detectable) |
| RADM-02 | Test 6 (update increments version) |
| RADM-03 | Test 7 (deactivate sets IsActive=false), Test 8 (deactivated not in active list) |
| D-14 | Test 12 (rejects invalid StageType), Test 13 (rejects invalid FieldType), Test 14 (accepts valid) |

## Test Results

All 14 tests pass: `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeEndpointTests"` exits 0.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed tenant lookup in provisioning tests to use company name not AdminEmail**
- **Found during:** Task 1
- **Issue:** Plan template helper used `t.AdminEmail` in the tenant lookup, but the `Tenant` entity has no `AdminEmail` property.
- **Fix:** Replaced `CreateApprovedSignupRequest` helper usage in Tests 10 and 11 with inline signup request creation that captures a unique `companyName` string for subsequent tenant lookup via `t.Name == companyName`.
- **Files modified:** `IronMonkey.Tests/Integration/RecipeEndpointTests.cs`
- **Commit:** a2ae1ad

## Self-Check: PASSED

- [x] `IronMonkey.Tests/Integration/RecipeEndpointTests.cs` exists
- [x] Commit a2ae1ad exists
- [x] 14 tests all pass
