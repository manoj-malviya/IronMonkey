---
phase: 10-recipe-management-ui
plan: "01"
subsystem: web-ui
tags: [blazor, recipes, admin, tailwind, http-client]
dependency_graph:
  requires: [09-03]
  provides: [recipe-list-page]
  affects: [IronMonkey.Web]
tech_stack:
  added: []
  patterns: [IHttpClientFactory-AdminApi, Razor-Authorize-guard, optimistic-UI-mutation]
key_files:
  created:
    - IronMonkey.Web/Components/Pages/Admin/Recipes/RecipeList.razor
  modified:
    - IronMonkey.Web/ApiClient.cs
    - IronMonkey.Web/Authentication/AdminAuthenticationStateProvider.cs
    - IronMonkey.Web/CircuitHandlers/IdentityValidationCircuitHandler.cs
    - IronMonkey.Web/Components/App.razor
    - IronMonkey.Web/Components/Layout/AdminSidebar.razor
    - IronMonkey.Web/Components/Layout/AdminTopbar.razor
    - IronMonkey.Web/Components/Layout/MainLayout.razor
    - IronMonkey.Web/Components/Pages/Admin/Index.razor
    - IronMonkey.Web/Components/Pages/Login.razor
    - IronMonkey.Web/Components/Routes.razor
    - IronMonkey.Web/Components/_Imports.razor
    - IronMonkey.Web/HttpHandlers/BearerTokenHandler.cs
    - IronMonkey.Web/IronMonkey.Web.csproj
    - IronMonkey.Web/Program.cs
    - IronMonkey.Web/wwwroot/css/input.css
decisions:
  - Use NavigateToPreview/NavigateToEdit helper methods instead of inline lambdas with interpolated strings — Razor HTML attributes cannot contain $"..." interpolated strings directly
  - RecipeListItem is a private class (not record) to allow optimistic IsActive=false mutation without replace
metrics:
  duration_minutes: 15
  completed_date: "2026-03-31"
  tasks_completed: 1
  files_changed: 21
---

# Phase 10 Plan 01: Recipe List Page Summary

RecipeList.razor at `/admin/recipes` with data table, status badge filtering, optimistic deactivate, and navigation to create/edit/preview routes.

## Objective

Build the RecipeList page — the main entry point for recipe management in the admin UI. Admins see all recipes in a table, can toggle deactivated recipes in/out, deactivate active recipes inline without a page reload, and navigate to create/edit/preview routes.

## What Was Built

**RecipeList.razor** (`IronMonkey.Web/Components/Pages/Admin/Recipes/RecipeList.razor`):
- `@page "/admin/recipes"` with `@attribute [Authorize]` and `@inject IHttpClientFactory HttpClientFactory`
- Two-row heading: h1 "Recipe Management" + Create Recipe button (indigo-600, Heroicon plus), and a "Show Deactivated Recipes" checkbox bound to `_showInactive`
- Error banner (red-50/red-200/red-700) and success banner (green-50/green-200/green-700)
- Loading state: centered "Loading recipes..." text
- Empty state: dashed border box with "No recipes found." and "Create your first recipe" CTA
- Data table with columns: Name, Industry, Status, Stages, Fields, Rules, Roles, Version, Actions
- Status badge: green (bg-green-100 text-green-800) for Active, slate (bg-slate-100 text-slate-600) for Inactive
- `DisplayedRecipes` computed property: when `_showInactive=false`, filters `_recipes.Where(r => r.IsActive)`
- Actions per row: Preview → `/admin/recipes/{id}/preview`, Edit → `/admin/recipes/{id}/edit`, Deactivate (shown only when `recipe.IsActive`)
- Deactivate calls DELETE `/api/recipes/{id}`, on success sets `item.IsActive = false` optimistically, shows success banner
- `HandleDeactivateAsync` tracks `_isDeactivating`/`_deactivatingId` for per-row loading state, shows "..." on in-progress row
- Handles 401 by redirecting to `/login`
- `RecipeListItem` is a private `class` (mutable) to support optimistic `IsActive = false` mutation

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| NavigateToPreview/NavigateToEdit as named methods | Razor HTML attributes cannot contain `$"..."` interpolated strings inline — extract to methods in @code block |
| RecipeListItem as class, not record | Records are immutable by default; mutation `item.IsActive = false` for optimistic update requires a mutable class |
| ShowInactive toggles DisplayedRecipes, not a server refetch | All recipes fetched once on load; deactivated ones hidden client-side. Backend currently only returns active recipes, so toggling shows items deactivated in this session only |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Phase 9 auth infrastructure missing from worktree**
- **Found during:** Task 1 setup
- **Issue:** This worktree was based on `main` (v1.0) which lacks Phase 9 auth infrastructure. `@attribute [Authorize]`, `IHttpClientFactory`, `AdminAuthenticationStateProvider`, `BearerTokenHandler`, `_Imports.razor` auth usings — all absent. Build had 10+ pre-existing errors.
- **Fix:** Checked out Phase 9 Web files from `v2` branch into this worktree: all auth/layout/infrastructure files needed for the new page to compile
- **Files modified:** 14 Phase 9 Web files brought into worktree + NuGet restore for `System.IdentityModel.Tokens.Jwt`
- **Commit:** b12a76c

**2. [Rule 1 - Bug] Inline lambda with interpolated string in Razor HTML attribute**
- **Found during:** Task 1 build verification (first attempt)
- **Issue:** `@onclick="() => Nav.NavigateTo($"/admin/recipes/{recipe.Id}/preview")"` — Razor parser cannot handle `$"..."` interpolated strings in HTML attribute values; fails with RZ9986/CS1056 errors
- **Fix:** Extracted to `NavigateToPreview(Guid id)` and `NavigateToEdit(Guid id)` helper methods in `@code` block; lambdas reference those methods instead
- **Files modified:** RecipeList.razor
- **Commit:** b12a76c (same commit, fixed before commit)

## Known Stubs

None. The page is fully wired:
- Loads from GET `/api/recipes` via `IHttpClientFactory.CreateClient("AdminApi")`
- Deactivates via DELETE `/api/recipes/{id}`
- Navigation buttons use real IDs from loaded data

Note: "Show Deactivated Recipes" toggle only reveals items deactivated in the **current session** (optimistic update). The backend GET `/api/recipes` filters to `IsActive == true` only, so items deactivated before this page load won't appear even with the toggle on. This is documented in the plan as the intended behavior for Phase 10.

## Self-Check: PASSED

- [x] `IronMonkey.Web/Components/Pages/Admin/Recipes/RecipeList.razor` exists
- [x] Commit b12a76c exists: `git log --oneline | grep b12a76c`
- [x] `dotnet build IronMonkey.Web` exits 0 (verified during execution)
- [x] All acceptance criteria checked: @page, [Authorize], GetFromJsonAsync, DeleteAsync, GetStatusBadgeClass, DisplayedRecipes, "Create your first recipe"
