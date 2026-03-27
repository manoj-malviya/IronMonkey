---
phase: 07-recipe-content
plan: "03"
subsystem: recipe-content
tags: [recipe, education, integration-tests, provisioning]
dependency_graph:
  requires: [07-01, 07-recipe-content/07-01]
  provides: [EducationRecipeProvisioningTests, Education recipe verification]
  affects: [IronMonkey.Tests/Integration, IronMonkey.Data/Migrations/Central]
tech_stack:
  added: []
  patterns: [recipe provisioning test pattern, fixed GUID test pattern for seeded recipes]
key_files:
  created:
    - IronMonkey.Tests/Integration/EducationRecipeProvisioningTests.cs
    - IronMonkey.Data/Migrations/Central/20260326120000_SeedDomainRecipes.Designer.cs
  modified: []
decisions:
  - "Domain roles (Admissions Director, Admissions Officer, Academic Counselor) are informational only in Phase 7 — TenantProvisioningService does not process the Roles section of RecipeContentModel; documented with TODO in SeedsRoles test"
  - "SeedDomainRecipes migration was missing its Designer.cs file (Rule 3 deviation) — created matching Designer.cs from SeedBlankRecipe.Designer.cs pattern to enable MigrateAsync() resolution"
metrics:
  duration: "~8 minutes"
  completed_date: "2026-03-26"
  tasks: 2
  files: 2
---

# Phase 07 Plan 03: Education Recipe Provisioning Tests Summary

**One-liner:** Integration tests verifying Education recipe provisioning creates all 6 pipeline stages (including "Under Review"), 6 custom fields (including "Grade/Year Level"), 2 workflow rules, platform roles, and 5 sample leads distributed 2+1+1+1 across admissions stages.

## What Was Built

### Task 1: Education recipe content tests (stages, fields, rules, roles)

Created `IronMonkey.Tests/Integration/EducationRecipeProvisioningTests.cs` with 4 test methods using fixed GUID `00000000-0000-0000-0000-000000000003`:

**SeedsAllPipelineStages:**
- Asserts exactly 6 stages in Order 0..5
- Verifies exact names including "Under Review" (with space) mapped to `StageType.Active`
- Verifies "Enrolled" mapped to `StageType.ClosedWon` and "Declined" mapped to `StageType.ClosedLost`

**SeedsAllCustomFields:**
- Asserts exactly 6 custom fields
- Verifies "Grade/Year Level" (with slash) as `CustomFieldType.Dropdown`
- Verifies "Program of Interest" as `Dropdown`, "Scholarship Needed" as `Boolean`, text fields

**SeedsWorkflowRules:**
- Asserts exactly 2 workflow rules
- Verifies "Notify on Review" with `WorkflowTrigger.StatusChange`
- Verifies "Flag Stale Inquiry" with `WorkflowTrigger.TimeElapsed`

**SeedsRoles:**
- Documents domain roles (Admissions Director, Officer, Counselor) as informational in Phase 7
- Verifies platform roles (Admin, SuperAdmin) seeded by EF migration are present

### Task 2: Education sample lead tests (RCNT-03)

Two additional test methods in the same file:

**SeedsSampleLeads:**
- Asserts exactly 5 leads
- Verifies distribution: 2 in Inquiry, 1 in Application, 1 in Under Review, 1 in Interview
- Asserts 0 leads in terminal stages (Enrolled, Declined)
- Verifies all leads have `LeadSource.WebForm`

**SampleLeadCustomFields:**
- Verifies Inquiry lead has "Program of Interest" populated
- Verifies Application lead has both "Program of Interest" and "Previous School"
- Verifies Under Review lead has "Program of Interest"

## Commits

| Task | Commit | Description |
|------|--------|-------------|
| 1+2 | b383acf | feat(07-03): add Education recipe content tests (stages, fields, rules, roles) |

Note: Both tasks were implemented in a single file commit since the complete file was written atomically. All 6 tests pass.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Missing SeedDomainRecipes.Designer.cs**
- **Found during:** Task 1 — first test run
- **Issue:** `20260326120000_SeedDomainRecipes.cs` was created in Plan 01 without a `.Designer.cs` file. EF Core requires the Designer file to locate and apply migrations via `MigrateAsync()`. Without it, the migration was not applied and the Education recipe GUID was not found.
- **Fix:** Created `20260326120000_SeedDomainRecipes.Designer.cs` by cloning `20260326065340_SeedBlankRecipe.Designer.cs` pattern and updating the migration name attribute to `"20260326120000_SeedDomainRecipes"`. Schema model is identical since SeedDomainRecipes only inserts data.
- **Files modified:** `IronMonkey.Data/Migrations/Central/20260326120000_SeedDomainRecipes.Designer.cs`
- **Commit:** b383acf

## Known Stubs

None — all 6 tests execute against real PostgreSQL containers and verify actual entity creation from the seeded Education recipe.

## Verification Results

- `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~EducationRecipeProvisioningTests"` — 6 passed, 0 failed
- `dotnet build IronMonkey.Tests.csproj` — 0 errors, 18 pre-existing warnings (all pre-Phase 7)
- Stage "Under Review" with space: exact string assertion passes
- Field "Grade/Year Level" with slash: exact string assertion passes
- Sample lead distribution (2+1+1+1): all count assertions pass

## Self-Check: PASSED

- [x] IronMonkey.Tests/Integration/EducationRecipeProvisioningTests.cs — created
- [x] IronMonkey.Data/Migrations/Central/20260326120000_SeedDomainRecipes.Designer.cs — created
- [x] Commit b383acf — verified via git log
- [x] All 6 EducationRecipeProvisioningTests pass
