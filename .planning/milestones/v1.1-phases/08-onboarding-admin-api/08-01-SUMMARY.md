---
phase: 08-onboarding-admin-api
plan: 01
subsystem: auth
tags: [signup, provisioning, recipes, entity-framework, migrations]

# Dependency graph
requires:
  - phase: 06-recipe-data-model
    provides: IndustryRecipe entity, RecipeId seeding, IsActive property
  - phase: 07-recipe-content
    provides: TenantProvisioningService with recipe-based SeedTenantDataAsync
provides:
  - SignupRequest entity with RecipeId (Guid?) replacing IndustryType string
  - EF migration Phase8_SignupRequestRecipeId drops IndustryType, adds RecipeId
  - POST /auth/signup accepts optional RecipeId (null = Blank recipe)
  - POST /tenants/{id}/provision reads RecipeId from SignupRequest and passes to provisioning
  - Deactivated-recipe guard in TenantProvisioningService throws with "deactivated" message
  - Blank recipe default (GUID 00000000-0000-0000-0000-000000000001) when RecipeId is null
affects:
  - 08-02-recipe-admin-api
  - 08-03-signup-admin-api

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Endpoint reads SignupRequest to extract RecipeId before calling provisioning service"
    - "effectiveRecipeId pattern: validate/guard on passed value or default to Blank recipe GUID"

key-files:
  created:
    - IronMonkey.Data/Migrations/Central/20260326182548_Phase8_SignupRequestRecipeId.cs
    - IronMonkey.Data/Migrations/Tenant/20260326182650_Phase8_SignupRequestRecipeId.cs
  modified:
    - IronMonkey.Data/Entities/SignupRequest.cs
    - IronMonkey.Data/Configurations/SignupRequestConfiguration.cs
    - IronMonkey.ApiService/Authentication/Endpoints/SignupRequestEndpoint.cs
    - IronMonkey.ApiService/Authentication/Endpoints/ProvisionTenantEndpoint.cs
    - IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs
    - IronMonkey.Tests/Integration/TenantProvisioningTests.cs
    - IronMonkey.Tests/Integration/AutomobileRecipeProvisioningTests.cs
    - IronMonkey.Tests/Integration/BlankRecipeProvisioningTests.cs
    - IronMonkey.Tests/Integration/EducationRecipeProvisioningTests.cs
    - IronMonkey.Tests/Integration/RecipeProvisioningTests.cs

key-decisions:
  - "effectiveRecipeId used in both SeedTenantDataAsync and Step 6a (SetAppliedRecipe) so Blank recipe is tracked when no RecipeId specified"
  - "ProvisionTenantEndpoint loads SignupRequest before calling provisioning service (not inside service) to allow early NotFound return"
  - "Tenant migration also created because ApplyConfigurationsFromAssembly picks up SignupRequestConfiguration in both contexts"

patterns-established:
  - "Endpoint pattern: load dependent entity first, return NotFound early, then call service with resolved fields"
  - "Service pattern: validate guard (is deactivated?) before expensive operations (SeedTenantDataAsync)"

requirements-completed:
  - ONBD-01
  - ONBD-03
  - ONBD-04

# Metrics
duration: 45min
completed: 2026-03-26
---

# Phase 8 Plan 01: Signup-to-Provision RecipeId Flow Summary

**RecipeId (Guid?) replaces IndustryType string across signup-to-provision flow — deactivated-recipe guard added to TenantProvisioningService with Blank recipe fallback**

## Performance

- **Duration:** ~45 min
- **Started:** 2026-03-26T18:20:00Z
- **Completed:** 2026-03-26T18:35:00Z
- **Tasks:** 2
- **Files modified:** 12

## Accomplishments

- SignupRequest entity now carries RecipeId (Guid?) from signup through to provisioning
- POST /auth/signup accepts optional RecipeId; missing RecipeId is valid and defaults to Blank
- POST /tenants/{id}/provision reads RecipeId from SignupRequest, passes to provisioning service, returns NotFound if SignupRequest not found
- TenantProvisioningService validates recipe IsActive before seeding; throws InvalidOperationException containing "deactivated" for inactive recipes
- EF migration drops IndustryType column, adds RecipeId nullable uuid column in central DB

## Task Commits

1. **Task 1: Replace IndustryType with RecipeId in SignupRequest entity and EF config** - `458fa50` (feat)
2. **Task 2: Update ProvisionTenantEndpoint and add deactivated-recipe guard** - `6859d91` (feat)

## Files Created/Modified

- `IronMonkey.Data/Entities/SignupRequest.cs` - RecipeId (Guid?) replaces IndustryType; Create() signature updated
- `IronMonkey.Data/Configurations/SignupRequestConfiguration.cs` - builder.Property(x => x.RecipeId) replaces IsRequired IndustryType
- `IronMonkey.Data/Migrations/Central/20260326182548_Phase8_SignupRequestRecipeId.cs` - drops IndustryType, adds RecipeId uuid nullable
- `IronMonkey.Data/Migrations/Tenant/20260326182650_Phase8_SignupRequestRecipeId.cs` - same for tenant DB snapshot
- `IronMonkey.ApiService/Authentication/Endpoints/SignupRequestEndpoint.cs` - Guid? RecipeId replaces string IndustryType in Request record
- `IronMonkey.ApiService/Authentication/Endpoints/ProvisionTenantEndpoint.cs` - injects CentralDbContext, loads SignupRequest.RecipeId, passes to service
- `IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs` - Step 4b deactivated-recipe guard; effectiveRecipeId defaults to Blank
- `IronMonkey.Tests/Integration/TenantProvisioningTests.cs` - updated SignupRequest.Create callers
- `IronMonkey.Tests/Integration/*RecipeProvisioningTests.cs` - updated all SignupRequest.Create callers (industryType -> recipeId: null)

## Decisions Made

- `effectiveRecipeId` variable used in both SeedTenantDataAsync call and Step 6a SetAppliedRecipe — ensures Blank recipe is tracked even when no RecipeId was provided in the original request
- ProvisionTenantEndpoint loads SignupRequest independently before calling provisioning service so it can return NotFound early without entering the provisioning transaction
- Both Central and Tenant EF migrations were needed because TenantDbContext uses ApplyConfigurationsFromAssembly which picks up SignupRequestConfiguration

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed duplicate MapIngestionEndpoints in Endpoints.cs**
- **Found during:** Task 1 (build verification)
- **Issue:** Endpoints.cs had two identical `private void MapIngestionEndpoints()` methods, causing CS0111 error that blocked build
- **Fix:** Removed the second duplicate block (lines 182-202)
- **Files modified:** IronMonkey.ApiService/Endpoints.cs
- **Verification:** dotnet build passes after removal
- **Committed in:** 458fa50 (Task 1 commit)

**2. [Rule 3 - Blocking] Merged Phase 6/7 code from v2 into worktree**
- **Found during:** Task 2 (implementing deactivated-recipe guard)
- **Issue:** Worktree was forked from v1.0 (commit e4b7008) and lacked IndustryRecipe entity, RecipeContentModel, and updated TenantProvisioningService from Phase 6/7
- **Fix:** Ran `git merge v2 --no-commit --no-ff` to bring in Phase 6/7 changes; resolved conflict in ProvisionTenantEndpoint.cs (kept Task 2 implementation)
- **Files modified:** All Phase 6/7 files now present (IndustryRecipe.cs, RecipeContentModel.cs, migrations, test files)
- **Verification:** dotnet build passes with all Phase 6/7 + Phase 8 code integrated
- **Committed in:** 6859d91 (Task 2 commit)

**3. [Rule 1 - Bug] Updated Phase 7 test files to use recipeId: null**
- **Found during:** Task 2 (after merging v2 code)
- **Issue:** Phase 7 test files (AutomobileRecipeProvisioningTests, BlankRecipeProvisioningTests, EducationRecipeProvisioningTests, RecipeProvisioningTests) still used `industryType:` named argument which no longer exists
- **Fix:** Replaced all `industryType: "..."` with `recipeId: null` in 4 test files
- **Files modified:** All four Phase 7 recipe provisioning test files
- **Verification:** dotnet build passes with 0 errors
- **Committed in:** 6859d91 (Task 2 commit)

---

**Total deviations:** 3 auto-fixed (1 blocking build error, 1 blocking missing Phase 6/7 base, 1 compilation bug)
**Impact on plan:** All fixes necessary for correctness. Merge of v2 was required to implement the plan as specified — the plan assumed Phase 6/7 code was present.

## Issues Encountered

- Worktree was created from v1.0 base commit (`e4b7008`) before Phase 6/7 changes were merged into v2. This meant the worktree lacked IndustryRecipe entity and recipe provisioning code. Resolved by merging v2 into the worktree.

## Next Phase Readiness

- Recipe selection is now wired end-to-end: signup captures RecipeId, provisioning reads it, validates recipe is active, seeds accordingly
- Deactivated-recipe guard is in place with correct error message
- All pre-existing provisioning tests updated and passing
- Ready for 08-02 (Recipe Admin API: list, activate/deactivate recipes)

---
*Phase: 08-onboarding-admin-api*
*Completed: 2026-03-26*
