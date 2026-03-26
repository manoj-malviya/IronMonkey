---
phase: 07-recipe-content
plan: "02"
subsystem: recipe-content
tags: [recipe, tests, integration, automobile, provisioning]
dependency_graph:
  requires: [07-01]
  provides: [AutomobileRecipeProvisioningTests, SeedDomainRecipes.Designer.cs]
  affects: [IronMonkey.Tests/Integration, IronMonkey.Data/Migrations/Central]
tech_stack:
  added: []
  patterns: [shared PostgreSqlFixture for integration tests, MigrateAsync for migration-seeded data testing]
key_files:
  created:
    - IronMonkey.Tests/Integration/AutomobileRecipeProvisioningTests.cs
    - IronMonkey.Data/Migrations/Central/20260326120000_SeedDomainRecipes.Designer.cs
  modified: []
decisions:
  - "SeedDomainRecipes.Designer.cs was missing from 07-01 commit — EF Core requires the designer snapshot file to recognize a migration; added it with same model as SeedBlankRecipe (pure data migration, no schema changes)"
  - "Roles test asserts 4 system roles (SuperAdmin/Admin/Owner/TeleCaller) only per Pitfall 5 — domain recipe RoleDefinitions are informational; custom roles are Phase 8 work"
  - "All 6 tests written atomically in one commit since Tasks 1 and 2 target the same file"
metrics:
  duration: "~11 minutes"
  completed_date: "2026-03-26"
  tasks: 2
  files: 2
---

# Phase 07 Plan 02: Automobile Recipe Provisioning Tests Summary

**One-liner:** 6 integration tests verifying Automobile Dealership recipe provisioning creates correct pipeline stages, custom fields, workflow rules, system roles, sample leads, and custom field values against real PostgreSQL with migration-seeded recipe data.

## What Was Built

### Task 1: Automobile recipe content tests (stages, fields, rules, roles)

Created `AutomobileRecipeProvisioningTests.cs` with 4 test methods:

- **SeedsAllPipelineStages** — Asserts 6 stages (Inquiry/Entry, Test Drive/Active, Negotiation/Active, F&I/Active, Sold/ClosedWon, Lost/ClosedLost) ordered 0..5
- **SeedsAllCustomFields** — Asserts 6 fields (Vehicle Make/Dropdown, Vehicle Model/Text, Vehicle Year/Number, Budget Range/Dropdown, Has Trade-In/Boolean, Preferred Contact/Dropdown)
- **SeedsWorkflowRules** — Asserts 2 rules (Notify on Negotiation/StatusChange, Flag Stale Inquiry/TimeElapsed)
- **SeedsRoles** — Asserts 4 system roles (SuperAdmin, Admin, Owner, TeleCaller) with TODO for Phase 8 custom roles

### Task 2: Automobile sample lead tests (RCNT-03)

Added 2 more test methods to the same file:

- **SeedsSampleLeads** — Asserts 5 total leads, distribution 2/1/1/1 across Inquiry/Test Drive/Negotiation/F&I, 0 in terminal stages, all source == WebForm
- **SampleLeadsHaveCustomFields** — Asserts Inquiry lead has Vehicle Make and Budget Range custom field values, Test Drive lead has Vehicle Make value

### Deviation: Missing SeedDomainRecipes.Designer.cs (Rule 3 — Blocking)

**Found during:** Task 1 test execution
**Issue:** `SeedDomainRecipes` migration was not recognized by EF Core because the `.Designer.cs` snapshot file was missing from the 07-01 commit. `dotnet ef migrations list` only showed migrations up to `SeedBlankRecipe`. Tests failed with "Recipe 00000000-0000-0000-0000-000000000002 not found" because `MigrateAsync()` skipped the seed migration.
**Fix:** Created `20260326120000_SeedDomainRecipes.Designer.cs` with the same `BuildTargetModel` as `SeedBlankRecipe` (pure data migration, no schema changes, same model snapshot).
**Files modified:** `IronMonkey.Data/Migrations/Central/20260326120000_SeedDomainRecipes.Designer.cs`
**Commit:** `491f78d`

### Deviation: Duplicate MapIngestionEndpoints in Endpoints.cs (Rule 3 — Blocking)

**Found during:** Initial build
**Issue:** `IronMonkey.ApiService/Endpoints.cs` had two `MapIngestionEndpoints()` methods (pre-existing worktree state — another parallel agent had added a duplicate). Caused `CS0111` build error preventing Tests from compiling.
**Fix:** Rebased worktree branch on `v2` which resolved the conflict (the duplicate was from pre-rebase state only). After rebase only one `MapIngestionEndpoints` method remained.
**Files modified:** None (resolved by rebase)
**Commit:** N/A (rebase, not a code change)

## Commits

| Task | Commit | Description |
|------|--------|-------------|
| 1+2  | 491f78d | feat(07-02): add AutomobileRecipeProvisioningTests (stages, fields, rules, roles) |

## Test Results

All 6 tests pass:
- ProvisionTenant_WithAutomobileRecipe_SeedsAllPipelineStages — PASS
- ProvisionTenant_WithAutomobileRecipe_SeedsAllCustomFields — PASS
- ProvisionTenant_WithAutomobileRecipe_SeedsWorkflowRules — PASS
- ProvisionTenant_WithAutomobileRecipe_SeedsRoles — PASS (4 system roles)
- ProvisionTenant_WithAutomobileRecipe_SeedsSampleLeads — PASS
- ProvisionTenant_WithAutomobileRecipe_SampleLeadsHaveCustomFields — PASS

BlankRecipeProvisioningTests (regression): 2 tests — PASS

## Known Stubs

None — all test assertions are against real seeded data.

## Self-Check: PASSED

- [x] IronMonkey.Tests/Integration/AutomobileRecipeProvisioningTests.cs — exists with 6 test methods
- [x] IronMonkey.Data/Migrations/Central/20260326120000_SeedDomainRecipes.Designer.cs — created
- [x] Commit 491f78d — verified via git log
- [x] All 6 AutomobileRecipeProvisioningTests pass
- [x] BlankRecipeProvisioningTests regression — 2 tests pass
