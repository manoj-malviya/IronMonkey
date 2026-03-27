---
phase: 06-recipe-data-model
plan: 03
subsystem: provisioning
tags: [provisioning, efcore, postgresql, recipe, integration-tests, atomicity]

# Dependency graph
requires:
  - plan: 06-01
    provides: IndustryRecipe entity, Tenant.SetAppliedRecipe(), IndustryRecipes DbSet
  - plan: 06-02
    provides: Blank recipe seeded with fixed GUID 00000000-0000-0000-0000-000000000001
provides:
  - TenantProvisioningService with optional Guid? recipeId parameter applying recipe content atomically
  - RecipeProvisioningTests (3 integration tests: stages, custom fields, AppliedRecipeId tracking)
  - BlankRecipeProvisioningTests (2 integration tests: blank recipe entry stage, null recipe backward compat)
  - Phase6_RecipeModel tenant DB migration (industry_recipes + tenant AppliedRecipe columns)
affects:
  - Phase 7 (recipe content): can test recipe application with real provisioning; provisioning service ready
  - Phase 8 (onboarding API): ProvisionTenantAsync accepts recipeId — signup endpoint can pass recipe selection

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Optional Guid? parameter with default null = backward-compatible interface extension"
    - "Enum.TryParse<T>() with fallback default for string-to-enum mapping from JSONB DTOs"
    - "Single SaveChangesAsync for recipe seeding = transactional atomicity (D-08)"
    - "ApplyConfigurationsFromAssembly in TenantDbContext picks up all configurations — tenant DB mirrors central entity shapes"

key-files:
  created:
    - IronMonkey.Tests/Integration/RecipeProvisioningTests.cs
    - IronMonkey.Tests/Integration/BlankRecipeProvisioningTests.cs
    - IronMonkey.Data/Migrations/Tenant/20260326070904_Phase6_RecipeModel.cs
    - IronMonkey.Data/Migrations/Tenant/20260326070904_Phase6_RecipeModel.Designer.cs
  modified:
    - IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs
    - IronMonkey.ApiService/Authentication/Endpoints/ProvisionTenantEndpoint.cs
    - IronMonkey.Data/Migrations/Tenant/TenantDbContextModelSnapshot.cs

key-decisions:
  - "Phase6_RecipeModel migration created for TenantDbContext (mirrors central DB change) because ApplyConfigurationsFromAssembly includes all IEntityTypeConfiguration<T> from assembly — tenant DBs get industry_recipes table and tenants.AppliedRecipeId columns"
  - "SetAppliedRecipe called after MarkProvisioned (Step 6a) — recipe tracking is separate from the provisioning state change for clarity"
  - "SeedTenantDataAsync changed from static to instance method to access _centralDb for recipe loading"

# Metrics
duration: 27min
completed: 2026-03-26
---

# Phase 06 Plan 03: Recipe Provisioning Integration Summary

**TenantProvisioningService extended with optional recipe ID parameter that atomically applies pipeline stages, custom fields, and workflow rules from IndustryRecipe JSONB content during tenant provisioning — all 117 tests pass**

## Performance

- **Duration:** 27 min
- **Started:** 2026-03-26T06:57:23Z
- **Completed:** 2026-03-26T07:24:50Z
- **Tasks:** 2 (TDD: RED test stubs + GREEN implementation)
- **Files modified:** 7

## Accomplishments

- Created RecipeProvisioningTests.cs with 3 integration tests covering pipeline stage seeding, custom field seeding, and AppliedRecipeId/Version tracking
- Created BlankRecipeProvisioningTests.cs with 2 integration tests covering blank recipe entry stage and null recipeId backward compatibility
- Extended ITenantProvisioningService interface with optional `Guid? recipeId = null` parameter (backward compatible — existing callers unchanged)
- Updated `ProvisionTenantAsync` to pass recipeId through to `SeedTenantDataAsync` and call `tenant.SetAppliedRecipe()` after provisioning
- Updated `SeedTenantDataAsync` to load IndustryRecipe from central DB, deserialize ContentJson, and apply stages/fields/rules in dependency order
- Created `Phase6_RecipeModel` tenant DB migration adding `industry_recipes` table and `tenants.AppliedRecipeId/AppliedRecipeVersion` columns
- All 117 tests pass (9 provisioning tests + 108 existing)

## Task Commits

1. **Task 1: TDD RED — integration test stubs** - `a5ec417` (test)
2. **Task 2: TDD GREEN — extend TenantProvisioningService** - `c8b59e2` (feat)

## Files Created/Modified

- `IronMonkey.Tests/Integration/RecipeProvisioningTests.cs` — 3 integration tests for recipe-based provisioning
- `IronMonkey.Tests/Integration/BlankRecipeProvisioningTests.cs` — 2 integration tests for blank recipe and null recipe
- `IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs` — Updated interface signature, ProvisionTenantAsync, SeedTenantDataAsync with recipe application logic
- `IronMonkey.ApiService/Authentication/Endpoints/ProvisionTenantEndpoint.cs` — Fixed cancellationToken named parameter after signature change
- `IronMonkey.Data/Migrations/Tenant/20260326070904_Phase6_RecipeModel.cs` — Adds industry_recipes table + AppliedRecipeId/Version columns to tenant DB
- `IronMonkey.Data/Migrations/Tenant/TenantDbContextModelSnapshot.cs` — Updated to include IndustryRecipe entity and Tenant recipe columns

## Decisions Made

- Phase6_RecipeModel migration created for TenantDbContext because `ApplyConfigurationsFromAssembly` picks up all entity configurations from `IronMonkey.Data` assembly (including `IndustryRecipeConfiguration` and `TenantConfiguration`), so tenant DBs must mirror the central DB schema changes. This is a pre-existing architectural pattern in the codebase, not a new decision.
- `SetAppliedRecipe` placed in Step 6a (after `MarkProvisioned`) for clear separation of concerns: provisioning state and recipe tracking are independent operations.
- `SeedTenantDataAsync` changed from `static` to instance method to access `_centralDb` for recipe loading.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed ProvisionTenantEndpoint passing CancellationToken as recipeId argument**
- **Found during:** Task 2 — first build after updating interface signature
- **Issue:** `ProvisionTenantEndpoint.cs` called `ProvisionTenantAsync(id, cancellationToken)` with old positional signature. After adding `Guid? recipeId` as second parameter, `cancellationToken` was being passed as the recipe ID.
- **Fix:** Changed to `ProvisionTenantAsync(id, cancellationToken: cancellationToken)` using named parameter.
- **Files modified:** `IronMonkey.ApiService/Authentication/Endpoints/ProvisionTenantEndpoint.cs`
- **Commit:** c8b59e2

**2. [Rule 3 - Blocking] Created Phase6_RecipeModel tenant DB migration**
- **Found during:** Task 2 verification — running provisioning tests against PostgreSQL container
- **Issue:** `TenantDbContext.MigrateAsync()` threw `PendingModelChangesWarning` because `ApplyConfigurationsFromAssembly` includes `IndustryRecipeConfiguration` and new Tenant fields from Plan 01, creating a snapshot mismatch for TenantDbContext.
- **Fix:** Generated `Phase6_RecipeModel` migration for TenantDbContext (adds `industry_recipes` table and `tenants.AppliedRecipeId/AppliedRecipeVersion`). This mirrors the central DB change but is needed because tenant DBs include all entity configurations.
- **Note on tooling:** `migrations has-pending-model-changes --no-build` gave false positives; rebuild resolved comparison.
- **Files modified:** `IronMonkey.Data/Migrations/Tenant/20260326070904_Phase6_RecipeModel.cs`, `TenantDbContextModelSnapshot.cs`
- **Commit:** c8b59e2

## Issues Encountered

- `dotnet ef migrations remove --force` inadvertently removed Phase5 migration files when the Phase6 migration state was inconsistent. Restored Phase5 files from git, then regenerated Phase6 cleanly.
- `migrations has-pending-model-changes --no-build` falsely reported pending changes due to stale build cache; full rebuild with `dotnet ef migrations has-pending-model-changes` (without `--no-build`) confirmed no pending changes.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- Phase 7 (recipe content): TenantProvisioningService is ready to accept recipe IDs for Automobile and Education recipes
- Phase 8 (onboarding API): signup endpoint can pass `recipeId` to `ProvisionTenantAsync` based on tenant's industry selection
- All provisioning tests green; recipe application is atomic and backward compatible

## Known Stubs

None — all provisioning logic is fully wired. Recipe content applies from real IndustryRecipe entities loaded from the central DB.

## Self-Check: PASSED

Files verified:
- FOUND: IronMonkey.Tests/Integration/RecipeProvisioningTests.cs
- FOUND: IronMonkey.Tests/Integration/BlankRecipeProvisioningTests.cs
- FOUND: IronMonkey.Data/Migrations/Tenant/20260326070904_Phase6_RecipeModel.cs
- FOUND: IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs (updated)
- FOUND: a5ec417 (test stubs)
- FOUND: c8b59e2 (feat implementation)

All 117 tests pass: `dotnet test IronMonkey.Tests` — Passed: 117, Failed: 0
