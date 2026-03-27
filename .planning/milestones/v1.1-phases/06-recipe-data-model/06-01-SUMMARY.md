---
phase: 06-recipe-data-model
plan: 01
subsystem: database
tags: [efcore, postgresql, jsonb, entity, migration, csharp]

# Dependency graph
requires:
  - phase: v1.0 completed
    provides: Entity base class, CentralDbContext, TenantConfiguration patterns
provides:
  - IndustryRecipe entity with JSONB ContentJson in CentralDbContext
  - RecipeContentModel with PipelineStageDefinition, CustomFieldDefinitionDto, WorkflowRuleDefinition, RoleDefinition DTOs
  - IndustryRecipeConfiguration with HasColumnType("jsonb") and unique IndustrySlug index
  - Tenant.AppliedRecipeId and Tenant.AppliedRecipeVersion nullable fields with SetAppliedRecipe() mutator
  - EF migration Phase6_RecipeModel creating industry_recipes table and tenant recipe tracking columns
  - IndustryRecipeEntityTests with 4 green integration tests (RCPE-01, RCPE-02, RCPE-04)
affects:
  - 06-02-PLAN (migration already created here - Phase6_RecipeModel; 06-02 can skip model migration, do seed migration only)
  - 06-03-PLAN (provisioning service can now reference IndustryRecipe and Tenant.SetAppliedRecipe)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Platform entity extends Entity (not BaseTenantEntity) — no TenantId on cross-tenant data"
    - "JSONB content stored as string property, HasColumnType(\"jsonb\") in configuration"
    - "Recipe content model uses string enums in DTOs (matching entity enums) for JSON round-trip"

key-files:
  created:
    - IronMonkey.Data/RecipeContent/RecipeContentModel.cs
    - IronMonkey.Data/Entities/IndustryRecipe.cs
    - IronMonkey.Data/Configurations/IndustryRecipeConfiguration.cs
    - IronMonkey.Data/Migrations/Central/20260326064810_Phase6_RecipeModel.cs
    - IronMonkey.Tests/Integration/IndustryRecipeEntityTests.cs
  modified:
    - IronMonkey.Data/CentralDbContext.cs
    - IronMonkey.Data/Entities/Tenant.cs
    - IronMonkey.Data/Configurations/TenantConfiguration.cs
    - IronMonkey.ApiService/Endpoints.cs

key-decisions:
  - "Migration created in Plan 01 (not 02) because tests use MigrateAsync() which requires migration to exist"
  - "IndustryRecipe extends Entity not BaseTenantEntity — recipes are platform-level, not tenant-scoped"
  - "ContentJson stored as string property with HasColumnType(jsonb) — matches WorkflowRule.ConditionJson pattern"

patterns-established:
  - "Platform-level entities use Entity base (not BaseTenantEntity)"
  - "JSONB columns: string property in entity + HasColumnType('jsonb') in configuration"

requirements-completed:
  - RCPE-01
  - RCPE-02
  - RCPE-04

# Metrics
duration: 7min
completed: 2026-03-26
---

# Phase 06 Plan 01: Recipe Data Model Summary

**IndustryRecipe entity with JSONB content model DTOs, EF configuration, tenant recipe tracking fields, and Phase6_RecipeModel migration — all 4 integration tests green**

## Performance

- **Duration:** 7 min
- **Started:** 2026-03-26T06:42:26Z
- **Completed:** 2026-03-26T06:49:34Z
- **Tasks:** 3
- **Files modified:** 9

## Accomplishments

- Created IndustryRecipe entity (platform-level, extends Entity, not BaseTenantEntity) with Create/UpdateContent/Deactivate factory methods and JSONB ContentJson field
- Created RecipeContentModel with 4 section DTO classes (PipelineStageDefinition, CustomFieldDefinitionDto, WorkflowRuleDefinition, RoleDefinition) for JSON round-tripping
- Extended Tenant with AppliedRecipeId/AppliedRecipeVersion nullable fields and SetAppliedRecipe() mutator; registered IndustryRecipes DbSet in CentralDbContext
- Created EF migration Phase6_RecipeModel (industry_recipes table + tenant columns); all 4 IndustryRecipeEntityTests pass green

## Task Commits

1. **Task 1: Wave 0 test stubs (RCPE-01, RCPE-02, RCPE-04)** - `81d0124` (test)
2. **Task 2: IndustryRecipe entity, RecipeContentModel DTOs, EF configuration** - `350e604` (feat)
3. **Task 3: Extend Tenant entity** - `75a263c` (feat)
4. **Deviation fixes: Phase6_RecipeModel migration + duplicate method bug** - `3946cde` (fix)

## Files Created/Modified

- `IronMonkey.Data/RecipeContent/RecipeContentModel.cs` - JSONB DTO types (PipelineStageDefinition, CustomFieldDefinitionDto, WorkflowRuleDefinition, RoleDefinition)
- `IronMonkey.Data/Entities/IndustryRecipe.cs` - Platform-level entity with Create/UpdateContent/Deactivate, JSONB ContentJson
- `IronMonkey.Data/Configurations/IndustryRecipeConfiguration.cs` - EF config with HasColumnType("jsonb"), unique IndustrySlug index
- `IronMonkey.Data/Migrations/Central/20260326064810_Phase6_RecipeModel.cs` - Creates industry_recipes table, adds AppliedRecipeId/AppliedRecipeVersion to tenants
- `IronMonkey.Tests/Integration/IndustryRecipeEntityTests.cs` - 4 integration tests (all green)
- `IronMonkey.Data/CentralDbContext.cs` - Added DbSet<IndustryRecipe> and ApplyConfiguration(new IndustryRecipeConfiguration())
- `IronMonkey.Data/Entities/Tenant.cs` - Added AppliedRecipeId, AppliedRecipeVersion, SetAppliedRecipe()
- `IronMonkey.Data/Configurations/TenantConfiguration.cs` - Mapped AppliedRecipeId and AppliedRecipeVersion
- `IronMonkey.ApiService/Endpoints.cs` - Removed duplicate MapIngestionEndpoints() method

## Decisions Made

- Migration created in Plan 01 rather than Plan 02 because tests use MigrateAsync() which requires the migration file to exist. Plan 02 needs only the seed migration for the Blank recipe.
- IndustryRecipe extends Entity (not BaseTenantEntity) — recipes are platform-level templates shared across tenants, not tenant-owned data.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Created Phase6_RecipeModel EF migration in Plan 01**
- **Found during:** Task 2 verification (running IndustryRecipeEntityTests)
- **Issue:** Tests call `MigrateAsync()` which throws `PendingModelChangesWarning` when the entity model has no corresponding migration. Plan originally assigned migration creation to Plan 02.
- **Fix:** Created migration `Phase6_RecipeModel` covering all model changes from Plan 01 (industry_recipes table + tenant columns). Plan 02 now only needs to create the seed migration for the Blank recipe.
- **Files modified:** IronMonkey.Data/Migrations/Central/20260326064810_Phase6_RecipeModel.cs, 20260326064810_Phase6_RecipeModel.Designer.cs, CentralDbContextModelSnapshot.cs
- **Verification:** All 4 IndustryRecipeEntityTests pass with `dotnet test`
- **Committed in:** `3946cde`

**2. [Rule 1 - Bug] Fixed duplicate MapIngestionEndpoints() in Endpoints.cs**
- **Found during:** Task 2 verification (compiling IronMonkey.Tests which depends on IronMonkey.ApiService)
- **Issue:** IronMonkey.ApiService had two identical private `MapIngestionEndpoints()` methods in the extension block (lines 137 and 182), causing CS0111 compiler error that blocked test compilation.
- **Fix:** Removed the duplicate method at line 182 (second occurrence, identical body to first).
- **Files modified:** IronMonkey.ApiService/Endpoints.cs
- **Verification:** `dotnet build IronMonkey.Tests` exits 0 with no errors
- **Committed in:** `3946cde`

---

**Total deviations:** 2 auto-fixed (1 blocking, 1 bug)
**Impact on plan:** Both fixes essential for test execution. Migration scope shift from Plan 02 is minor — Plan 02 now creates only the seed migration for Blank recipe. No scope creep.

## Issues Encountered

- git stash pop conflict during pre-existing build error investigation caused CentralDbContext.cs to revert; re-applied the IndustryRecipes DbSet and ApplyConfiguration changes.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Plan 02 (migrations) should create only the Blank recipe seed migration — the Phase6_RecipeModel schema migration is already applied here
- Plan 03 (provisioning) can reference IndustryRecipe and Tenant.SetAppliedRecipe() — both are available
- All 4 integration tests passing green; foundation is solid

## Self-Check: PASSED

All files verified present, all commit hashes verified in git log:
- FOUND: RecipeContentModel.cs
- FOUND: IndustryRecipe.cs
- FOUND: IndustryRecipeConfiguration.cs
- FOUND: Phase6_RecipeModel migration
- FOUND: IndustryRecipeEntityTests.cs
- FOUND: 81d0124 (test stubs)
- FOUND: 350e604 (entity implementation)
- FOUND: 75a263c (tenant extension)
- FOUND: 3946cde (migration + bug fix)

---
*Phase: 06-recipe-data-model*
*Completed: 2026-03-26*
