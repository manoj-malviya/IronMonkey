---
phase: 10-recipe-management-ui
plan: "03"
subsystem: admin-ui
tags: [blazor, recipe-management, edit-form, read-only-preview, tailwind]
dependency_graph:
  requires: [10-02]
  provides: [recipe-edit-page, recipe-preview-page]
  affects: [admin-recipe-crud-cycle]
tech_stack:
  added: []
  patterns: [dual-api-fetch-merge, named-navigation-methods, record-with-expression-update]
key_files:
  created:
    - IronMonkey.Web/Components/Pages/Admin/Recipes/RecipeEdit.razor
    - IronMonkey.Web/Components/Pages/Admin/Recipes/RecipePreview.razor
  modified: []
decisions:
  - Dual API fetch on RecipeEdit init (GET /api/recipes/{id} + GET /api/recipes) to merge content and metadata fields since no single endpoint provides both
  - Metadata fields rendered read-only with explanatory note since PUT /api/recipes/{id} only accepts Content
  - RecipeListItem as record to enable `with` expression for optimistic version update after save
  - NavigateToEdit/NavigateToPreview as named methods to avoid Razor inline string interpolation limitation in HTML attributes
metrics:
  duration_seconds: 196
  completed_date: "2026-04-01"
  tasks_completed: 2
  tasks_total: 2
  files_created: 2
  files_modified: 0
---

# Phase 10 Plan 03: Recipe Edit and Preview Pages Summary

**One-liner:** RecipeEdit pre-populates from dual API fetch and saves content via PUT; RecipePreview shows five-tab read-only inspection view with Stages, Fields, Rules, Roles, and Sample Leads tables.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Create RecipeEdit.razor | b3da4fd | IronMonkey.Web/Components/Pages/Admin/Recipes/RecipeEdit.razor |
| 2 | Create RecipePreview.razor | 060e6a2 | IronMonkey.Web/Components/Pages/Admin/Recipes/RecipePreview.razor |

## What Was Built

### RecipeEdit.razor (`/admin/recipes/{Id:guid}/edit`)

- On init: fetches GET /api/recipes/{Id} for content and GET /api/recipes (list) for metadata fields (IndustrySlug, IconIdentifier, Version)
- Metadata (Name, Industry, Icon, Version) displayed read-only in a card with note that only content can be changed
- RecipeContentEditor component wired with the merged `_content` model for editing stages, fields, rules, and roles
- Save button PUTs `{ Content }` to /api/recipes/{Id}; success banner shows new version; 401 redirects to /login; 404 shows error banner
- Optimistic version update: `_listItem = _listItem with { Version = result.NewVersion }` after successful save
- Preview navigation button links to /admin/recipes/{Id}/preview

### RecipePreview.razor (`/admin/recipes/{Id:guid}/preview`)

- Loads recipe from GET /api/recipes/{Id} on init; no EditForm or input elements — purely read-only
- Five tabs: Stages (ordered by Order), Fields (with Required checkbox and Options), Rules (ConditionJson/ActionJson in monospace code), Roles, Sample Leads
- Each tab shows a table or "No X defined" empty state message
- Back to Recipes button + Edit Recipe button for navigation
- Tab switching via `_activeTab` string field toggled by tab button clicks

## Decisions Made

1. **Dual API fetch on RecipeEdit init** — GET /api/recipes/{id} returns only Id, Name, Description, Content. IconIdentifier, IndustrySlug, Version live only in the list endpoint. Fetching both on init and merging avoids requiring a backend schema change.

2. **Metadata read-only with note** — PUT /api/recipes/{id} only accepts RecipeContentModel Content. Rather than hiding metadata, it is shown as read-only with an explanatory note. This is correct and honest UX.

3. **RecipeListItem as record** — Required for `_listItem with { Version = ... }` expression to work after save response. Class cannot use `with`.

4. **Named navigation methods** — Razor HTML attributes cannot contain C# string interpolation (e.g. `@onclick='() => Nav.NavigateTo($"...")'` causes parser issues). Using `NavigateToPreview()` and `NavigateToEdit()` named methods resolves this cleanly. (Same pattern used in Plan 01/02.)

## Deviations from Plan

None — plan executed exactly as written.

## Known Stubs

None — both pages wire live API data. No hardcoded empty values that flow to UI rendering.

## Self-Check: PASSED

- RecipeEdit.razor exists: FOUND
- RecipePreview.razor exists: FOUND
- Commit b3da4fd exists: FOUND
- Commit 060e6a2 exists: FOUND
- `dotnet build IronMonkey.Web` exits with code 0: VERIFIED (0 errors, 2 pre-existing warnings)
