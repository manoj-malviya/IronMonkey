---
phase: 14-landing-page
plan: "03"
subsystem: ui
tags: [blazor, routing, auth, public-pages, layout]

# Dependency graph
requires:
  - 14-01 (PublicLayout.razor must exist)
  - 14-02 (Home.razor must exist at /)
provides:
  - Routes.razor updated: public pages (/, /login, /signup) excluded from NotAuthorized redirect
  - Login.razor updated: uses @layout PublicLayout, no AdminSidebar on login page
affects: [15-tenant-signup-flow]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - NotAuthorized path-check pattern for excluding public routes from redirect
    - "@layout directive on page overrides DefaultLayout in Routes.razor"

key-files:
  created: []
  modified:
    - IronMonkey.Web/Components/Routes.razor
    - IronMonkey.Web/Components/Pages/Login.razor

key-decisions:
  - "Path-check approach in NotAuthorized block: extract path from NavigationManager.Uri, suppress redirect for /, /login, /signup"
  - "Login.razor uses @layout IronMonkey.Web.Components.Layout.PublicLayout as line 2 — overrides DefaultLayout=MainLayout"

patterns-established:
  - "Public route exclusion pattern: var path = Nav.Uri.Replace(Nav.BaseUri, '/'').TrimEnd('/'); check before redirect"

requirements-completed: [NAV-01, LAND-03]

# Metrics
duration: 2min
completed: 2026-04-03
---

# Phase 14 Plan 03: Routing Fix and Login Layout Update Summary

**Routes.razor updated with public path exclusion in NotAuthorized block; Login.razor adopts @layout PublicLayout — both public pages now accessible to unauthenticated visitors without AdminSidebar**

## Performance

- **Duration:** 2 min
- **Started:** 2026-04-03T17:49:56Z
- **Completed:** 2026-04-03T17:51:31Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Routes.razor NotAuthorized block now checks current path before redirecting; /, /login, and /signup are excluded from the redirect — unauthenticated visitors can access public pages
- Login.razor receives `@layout IronMonkey.Web.Components.Layout.PublicLayout` as line 2 — AdminSidebar and AdminTopbar no longer render on the login page
- All existing login form markup, @code block, validation, and error handling preserved unchanged
- Build verified: 0 errors, 2 pre-existing warnings (unrelated)

## Task Commits

Each task was committed atomically:

1. **Task 1: Fix Routes.razor — allow unauthenticated access to public pages** - `05c1361` (feat)
2. **Task 2: Update Login.razor to use PublicLayout** - `18bdde4` (feat)

## Files Created/Modified

- `IronMonkey.Web/Components/Routes.razor` — Added path check in NotAuthorized block; /, /login, /signup excluded from redirect
- `IronMonkey.Web/Components/Pages/Login.razor` — Added `@layout IronMonkey.Web.Components.Layout.PublicLayout` as line 2

## Decisions Made

- Path-check approach: extract path via `Nav.Uri.Replace(Nav.BaseUri, "/").TrimEnd('/')` then compare against public route strings. This is explicit and extensible — adding Phase 15 /signup only required adding one more condition.
- Login.razor @layout directive approach: single line addition at top of file, all existing content preserved. The @layout directive on a page always overrides the DefaultLayout set in Routes.razor.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Public routing is now correct: /, /login, /signup accessible without auth
- Login page renders in PublicLayout (clean public shell, no admin nav)
- Phase 14 Plan 04 can proceed (pricing page or next public page)
- Phase 15 (tenant signup flow) can add /signup page knowing the route is already excluded from auth redirect

## Self-Check: PASSED

- FOUND: IronMonkey.Web/Components/Routes.razor (contains path-check logic)
- FOUND: IronMonkey.Web/Components/Pages/Login.razor (contains @layout PublicLayout on line 2)
- FOUND: .planning/phases/14-landing-page/14-03-SUMMARY.md
- FOUND: commit 05c1361 (Task 1)
- FOUND: commit 18bdde4 (Task 2)

---
*Phase: 14-landing-page*
*Completed: 2026-04-03*
