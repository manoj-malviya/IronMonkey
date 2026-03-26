---
phase: 08-onboarding-admin-api
plan: 02
subsystem: recipe-api
tags: [api, recipes, admin, anonymous, crud]
dependency_graph:
  requires:
    - 06-01 (IndustryRecipe entity, CentralDbContext.IndustryRecipes)
    - 07-01 (RecipeContentModel, RecipeContent namespace)
  provides:
    - GET /api/recipes (anonymous list)
    - GET /api/recipes/{id} (anonymous preview)
    - POST /api/recipes (admin create)
    - PUT /api/recipes/{id} (admin update)
    - DELETE /api/recipes/{id} (admin deactivate)
    - RecipeContentValidator (shared validator)
  affects:
    - 08-03 (signup endpoint can now call GET /api/recipes for recipe selection)
tech_stack:
  added: []
  patterns:
    - IEndpoint minimal API pattern
    - FluentValidation with SetValidator for nested models
    - AllowAnonymous vs RequireAuthorization split within same route group
key_files:
  created:
    - IronMonkey.ApiService/Features/Recipes/RecipeContentValidator.cs
    - IronMonkey.ApiService/Features/Recipes/RecipeListEndpoint.cs
    - IronMonkey.ApiService/Features/Recipes/RecipePreviewEndpoint.cs
    - IronMonkey.ApiService/Features/Recipes/CreateRecipeEndpoint.cs
    - IronMonkey.ApiService/Features/Recipes/UpdateRecipeEndpoint.cs
    - IronMonkey.ApiService/Features/Recipes/DeactivateRecipeEndpoint.cs
  modified:
    - IronMonkey.ApiService/Endpoints.cs
decisions:
  - "Read endpoints (GET list, GET preview) are AllowAnonymous to support signup recipe browsing without auth"
  - "Write endpoints use RecipeContentValidator via SetValidator to reuse stage/field type validation"
  - "MapRecipeEndpoints uses two separate route groups (public/admin) over /api/recipes to apply different auth policies"
  - "Full content replacement on PUT (no partial updates) — simpler API surface per D-08 decision"
metrics:
  duration_seconds: 625
  completed_date: "2026-03-26"
  tasks_completed: 2
  tasks_total: 2
  files_created: 6
  files_modified: 1
---

# Phase 08 Plan 02: Recipe API Endpoints Summary

Five recipe CRUD endpoints with shared content validator registered in the Minimal API application — anonymous read endpoints for signup flow and authorized write endpoints for platform admin.

## What Was Built

**RecipeContentValidator** — FluentValidation validator for `RecipeContentModel` that rejects invalid `StageType` values (must be Entry/Active/ClosedWon/ClosedLost) and invalid `FieldType` values (must be Text/Number/Date/Dropdown/MultiSelect/Currency/Boolean). Used by both Create and Update endpoints via `SetValidator`.

**GET /api/recipes** (anonymous) — Lists all active recipes ordered by `IsBlank` descending then `Name` ascending. Returns metadata including `StageCount`, `FieldCount`, `RuleCount`, `RoleCount` computed from deserialized `ContentJson`. Powers the signup recipe selection screen.

**GET /api/recipes/{id}** (anonymous) — Returns full `RecipeContentModel` content for a single active recipe. Required for tenants to preview recipe details before selecting.

**POST /api/recipes** (authorized) — Creates a new recipe using `IndustryRecipe.Create()`. Validates uniqueness of `IndustrySlug` among active recipes before creating. Validates full content via `RecipeContentValidator`.

**PUT /api/recipes/{id}** (authorized) — Full content replacement via `recipe.UpdateContent(content)` which auto-increments `Version`. Returns the new version number in response.

**DELETE /api/recipes/{id}** (authorized) — Soft deletes by calling `recipe.Deactivate()` which sets `IsActive = false`. Recipe remains in DB for historical reference.

**Endpoints.cs** updated with `MapRecipeEndpoints()` extension method that registers both public and admin route groups.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | RecipeContentValidator + anonymous read endpoints | 6db4dca | RecipeContentValidator.cs, RecipeListEndpoint.cs, RecipePreviewEndpoint.cs |
| 2 | Admin write endpoints + Endpoints.cs registration | 1adf5cd | CreateRecipeEndpoint.cs, UpdateRecipeEndpoint.cs, DeactivateRecipeEndpoint.cs, Endpoints.cs |

## Verification Results

- `dotnet build IronMonkey.ApiService` — Build succeeded, 0 errors
- `dotnet test IronMonkey.Tests` — 129 tests passed, 0 failed (no regressions)
- All 5 endpoint files + 1 validator exist in `IronMonkey.ApiService/Features/Recipes/`
- `MapRecipeEndpoints` registered in `MapEndpoints()` in `Endpoints.cs`

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Worktree missing Phase 6/7 code (RecipeContent namespace not present)**

- **Found during:** Task 1 (first build attempt)
- **Issue:** This worktree was branched from v1.0 (before Phase 6), so `IronMonkey.Data/RecipeContent/` did not exist, causing `CS0234` errors for `IronMonkey.Data.RecipeContent` namespace
- **Fix:** Rebased worktree branch onto v2 (`git rebase v2`) to include all Phase 6 and 7 code
- **Files modified:** None (rebase operation, no code changes)
- **Result:** RecipeContent namespace available, build succeeded

## Known Stubs

None — all endpoints wire to real database queries and domain entity methods.

## Self-Check: PASSED
