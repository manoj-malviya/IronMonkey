---
phase: 15-tenant-signup-flow
plan: 02
subsystem: ui
tags: [blazor, tailwind, signup-form, recipe-picker, public-page]

# Dependency graph
requires:
  - 15-01 (RecipePreviewCard.razor, RecipePreviewPanel.razor components)
provides:
  - Signup.razor — public tenant signup form at /signup with 7-field EditForm and recipe browser
affects:
  - 15-03 (SignupSuccessPage.razor needs /signup/success route; Routes.razor already allows it)
  - Login.razor (cross-link to /signup — already present)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - EditForm + DataAnnotationsValidator + ValidationMessage for all fields (same as Login.razor)
    - HttpClientFactory.CreateClient("AdminApi") for API calls
    - Toggle-selection recipe browser: Guid.Empty = blank, non-empty = real recipe ID
    - RecipePreviewPanel.RecipePreviewResponse as cross-component type reference for API deserialization

key-files:
  created:
    - IronMonkey.Web/Components/Pages/Signup.razor
  modified: []

key-decisions:
  - "Blank recipe uses Guid.Empty sentinel — no RecipeId sent in POST body when blank is selected (RecipeId = null)"
  - "Recipe toggle: clicking already-selected card sets _selectedRecipeId = Guid.Empty and clears preview (deselects)"
  - "Filter IsBlank recipes from _recipes list — blank option rendered manually as first card with fixed copy"
  - "Recipe browser silently fails on load error — optional feature, form still usable without recipes"

# Metrics
duration: 4min
completed: 2026-04-03
---

# Phase 15 Plan 02: Signup.razor Summary

**Public tenant signup form at /signup with 7 validated fields, recipe browser with toggle-select and preview panel, POST /auth/signup integration with loading/error states, and navigation to /signup/success on 201 success**

## Performance

- **Duration:** ~4 min
- **Started:** 2026-04-03T18:28:58Z
- **Completed:** 2026-04-03T18:31:56Z
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments

- Signup.razor renders a centered form card at /signup with PublicLayout
- All 7 fields have labels, placeholders, and inline ValidationMessage components with spec-exact copy
- CompanySize uses InputSelect dropdown with 5 options (1-10, 11-50, 51-200, 201-500, 500+)
- Recipe browser loads GET /api/recipes on OnInitializedAsync, filters out blank recipe, renders blank option first as manual card
- Recipe card click toggles selection; clicking a selected card deselects and clears preview
- Non-blank recipe click fetches GET /api/recipes/{id} and shows RecipePreviewPanel with loading indicator
- POST /auth/signup on valid submit: 201 navigates to /signup/success, 400 shows "Please check details", 409/422 shows "email already exists", other shows generic error, catch shows "Unable to reach server"
- Submit button shows "Submitting..." disabled state during loading
- Cross-link "Already have an account? Sign in" below form card

## Task Commits

1. **Task 1: Signup.razor — form + recipe browser** - `1fd7989` (feat)

## Files Created/Modified

- `IronMonkey.Web/Components/Pages/Signup.razor` — 296 lines; complete signup form with recipe browser; self-contained RecipeListItem record; SignupModel class with DataAnnotations

## Decisions Made

- Blank recipe uses Guid.Empty sentinel value — toggling blank card deselects any recipe and sets RecipeId to null in POST body
- Recipe list filters out IsBlank=true entries; blank "Start blank" card is rendered manually as the first card per D-07
- Silently catch recipe load failures — recipe browser is optional and form remains fully usable without it
- RecipePreviewPanel.RecipePreviewResponse used as deserialization type (public record defined in RecipePreviewPanel.razor @code block)

## Deviations from Plan

### Auto-noted (no changes required)

**1. [Rule 2 - Missing functionality check] Login.razor cross-link and Routes.razor public paths**
- Found during: Task 1 review
- Issue: Plan required Login.razor cross-link (D-13) and Routes.razor to allow /signup/success
- Finding: Both were already present — Login.razor already has "Don't have an account? Sign up" link (lines 73-75), Routes.razor already includes `path != "/signup/success"` in public path exclusion
- Action: No changes needed — no deviation from expected state

## Known Stubs

None — all fields wire to the EditForm model, recipe list wires to API call, form POST wires to navigation.

## Issues Encountered

None — Signup.razor compiled cleanly on first attempt (0 errors, 0 warnings).

## Self-Check: PASSED

- FOUND: IronMonkey.Web/Components/Pages/Signup.razor
- FOUND: commit 1fd7989

---

*Phase: 15-tenant-signup-flow*
*Completed: 2026-04-03*
