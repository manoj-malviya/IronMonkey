---
phase: 06-recipe-data-model
plan: 02
subsystem: database
tags: [efcore, postgresql, migration, seed, recipe]

# Dependency graph
requires:
  - plan: 06-01
    provides: Phase6_RecipeModel migration (industry_recipes table + tenant recipe columns)
provides:
  - SeedBlankRecipe EF Core migration inserting the Blank/Custom recipe row
  - Fixed GUID 00000000-0000-0000-0000-000000000001 for Blank recipe (predictable in provisioning)
  - ContentJson with PascalCase keys matching RecipeContentModel for correct deserialization
affects:
  - 06-03-PLAN (TenantProvisioningService can reference fixed Blank recipe GUID and expect MigrateAsync to apply seed)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Seed data via EF Core migration InsertData — deterministic on first deploy"
    - "Fixed GUID pattern for known-constant entities (Blank recipe)"
    - "PascalCase JSON keys required for System.Text.Json round-trip to RecipeContentModel"

key-files:
  created:
    - IronMonkey.Data/Migrations/Central/20260326065340_SeedBlankRecipe.cs
    - IronMonkey.Data/Migrations/Central/20260326065340_SeedBlankRecipe.Designer.cs
  modified: []

key-decisions:
  - "Task 1 (Phase6_RecipeModel migration) was already created in Plan 01 as a blocking deviation — Plan 02 created only the seed migration"
  - "Fixed GUID 00000000-0000-0000-0000-000000000001 for Blank recipe makes provisioning code deterministic without a DB lookup"
  - "PascalCase JSON keys in ContentJson are critical — System.Text.Json default does not apply camelCase conversion"

patterns-established:
  - "EF Core InsertData for seed rows in Up(); DeleteData in Down() for clean rollback"

requirements-completed:
  - RCPE-03
  - RCPE-04

# Metrics
duration: 4min
completed: 2026-03-26
---

# Phase 06 Plan 02: EF Core Migrations — Schema and Blank Recipe Seed Summary

**SeedBlankRecipe EF Core migration seeding one Blank/Custom industry recipe row with fixed GUID and PascalCase ContentJson**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-26T06:51:10Z
- **Completed:** 2026-03-26T06:55:10Z
- **Tasks:** 2 (1 verified-already-done, 1 executed)
- **Files modified:** 2

## Accomplishments

- Verified Task 1 (Phase6_RecipeModel schema migration) was fully completed as a deviation in Plan 01 — industry_recipes table and AppliedRecipeId/AppliedRecipeVersion columns on tenants all present
- Generated SeedBlankRecipe migration scaffold via `dotnet ef migrations add`
- Populated Up() with InsertData for Blank/Custom recipe: fixed GUID, PascalCase ContentJson, IsBlank=true, IndustrySlug='blank', Version=1, IsActive=true
- Populated Down() with DeleteData for clean rollback
- Build verified: `dotnet build IronMonkey.Data` exits 0
- Migrations list verified: both Phase6_RecipeModel and SeedBlankRecipe appear in order

## Task Commits

1. **Task 1: Generate schema migration** — ALREADY DONE in Plan 01 (commit `3946cde`)
2. **Task 2: Seed Blank/Custom recipe migration** — `e04a7b5` (feat)

## Files Created/Modified

- `IronMonkey.Data/Migrations/Central/20260326065340_SeedBlankRecipe.cs` — InsertData for Blank recipe (Up), DeleteData (Down)
- `IronMonkey.Data/Migrations/Central/20260326065340_SeedBlankRecipe.Designer.cs` — EF snapshot descriptor

## Decisions Made

- Task 1 skipped: Phase6_RecipeModel migration was created in Plan 01 to unblock integration tests that call MigrateAsync(). This plan needed only the seed migration.
- Fixed GUID 00000000-0000-0000-0000-000000000001 chosen to make Blank recipe identity deterministic — provisioning code and tests can reference this ID without a DB lookup.
- PascalCase JSON keys enforced per plan spec (D-15): System.Text.Json deserializes to PipelineStages/CustomFields/WorkflowRules/Roles without camelCase option; wrong case produces empty lists.

## Deviations from Plan

### Task 1: Already Completed

**Task 1 (Generate Phase6_RecipeModel schema migration)** was created as a Rule 3 (blocking) auto-fix deviation in Plan 01. The migration `20260326064810_Phase6_RecipeModel.cs` exists, creates `industry_recipes` table, and adds `AppliedRecipeId`/`AppliedRecipeVersion` to `tenants`. All plan done-criteria verified passing.

No other deviations — Plan 02 Task 2 executed exactly as specified.

## Known Stubs

None — seed data is complete and wire-ready.

## Self-Check: PASSED

- FOUND: IronMonkey.Data/Migrations/Central/20260326065340_SeedBlankRecipe.cs
- FOUND: IronMonkey.Data/Migrations/Central/20260326065340_SeedBlankRecipe.Designer.cs
- FOUND: e04a7b5 (seed migration commit)
- FOUND: Phase6_RecipeModel in migrations list
- FOUND: SeedBlankRecipe in migrations list
- FOUND: InsertData in SeedBlankRecipe.cs
- FOUND: "Blank/Custom" in SeedBlankRecipe.cs
- FOUND: fixed GUID 00000000-0000-0000-0000-000000000001 in SeedBlankRecipe.cs
- FOUND: PipelineStages (PascalCase) in SeedBlankRecipe.cs
- BUILD: IronMonkey.Data exits 0

---
*Phase: 06-recipe-data-model*
*Completed: 2026-03-26*
