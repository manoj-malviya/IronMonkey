---
phase: 09-ui-foundation-auth
plan: 03
subsystem: ui
tags: [blazor, tailwind, auth, sidebar, login, routing]

# Dependency graph
requires:
  - phase: 09-ui-foundation-auth/09-01
    provides: Tailwind CSS MSBuild pipeline + App.razor using output.css
  - phase: 09-ui-foundation-auth/09-02
    provides: AdminAuthenticationStateProvider, BearerTokenHandler, named "AdminApi" HttpClient, CascadingAuthenticationState

provides:
  - Login.razor: /login page with centered card, inline validation, loading state, JWT login flow
  - AdminSidebar.razor: 4 nav groups with Heroicons, active state highlight, user footer, logout
  - AdminTopbar.razor: mobile-only slim bar with hamburger icon
  - MainLayout.razor: admin shell with sidebar + topbar + content area, responsive desktop/mobile
  - Admin/Index.razor: auth-guarded /admin dashboard with placeholder stat cards
  - Routes.razor: AuthorizeRouteView with NotAuthorized redirect to /login

affects:
  - All Phase 10-13 admin pages (render inside this MainLayout shell)
  - Tenant, Recipe, User, Signup admin pages (navigate via AdminSidebar links)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Blazor EditForm + DataAnnotationsValidator for client-side validation without FluentValidation
    - AdminAuthenticationStateProvider cast from AuthenticationStateProvider for LoginAsync/LogoutAsync
    - IHttpClientFactory.CreateClient("AdminApi") for authenticated API calls in Razor pages
    - JS.InvokeAsync<int>("eval", "window.innerWidth") for viewport-based mobile/desktop detection
    - AuthorizeRouteView with NotAuthorized block for router-level auth guarding

key-files:
  created:
    - IronMonkey.Web/Components/Pages/Login.razor
    - IronMonkey.Web/Components/Layout/AdminSidebar.razor
    - IronMonkey.Web/Components/Layout/AdminTopbar.razor
    - IronMonkey.Web/Components/Pages/Admin/Index.razor
  modified:
    - IronMonkey.Web/Components/Layout/MainLayout.razor
    - IronMonkey.Web/Components/Routes.razor

key-decisions:
  - "Routes.razor uses AuthorizeRouteView (not RouteView) with NotAuthorized block — Nav.NavigateTo('/login') in NotAuthorized fires before any page renders, providing router-level auth gate"
  - "AdminSidebar IsMobile parameter set from JS window.innerWidth in MainLayout.OnAfterRenderAsync — single JS call on first render, avoids repeated JS interop"
  - "Login.razor uses readonly LoginModel field (_model = new()) — ensures clean form state on each page render (D-12)"
  - "Admin/Index.razor dashboard stat cards show em-dash placeholders — Phase 10-13 will wire real API counts"

patterns-established:
  - "Auth guard pattern: @attribute [Authorize] on admin pages + AuthorizeRouteView NotAuthorized redirect to /login"
  - "Sidebar/topbar shell: MainLayout orchestrates AdminTopbar (mobile) + AdminSidebar (responsive) + Body content"

requirements-completed: [UIFN-01, UIFN-03, UIFN-04, UIFN-06]

# Metrics
duration: 4min
completed: 2026-04-01
---

# Phase 9 Plan 03: Admin UI Shell — Login, Sidebar, Layout, Auth Guards Summary

**Login page with JWT auth flow, AdminSidebar with 4 Heroicon nav groups, mobile topbar, MainLayout shell, auth-guarded /admin index, and router-level NotAuthorized redirect to /login**

## Performance

- **Duration:** 4 min
- **Started:** 2026-04-01T03:12:02Z
- **Completed:** 2026-04-01T03:16:09Z
- **Tasks:** 2 (+ 1 checkpoint auto-approved)
- **Files modified:** 6

## Accomplishments

- Login.razor renders at /login with centered card on slate gradient, inline DataAnnotations validation on Email + Password fields, summary error banner for 401 responses, "Signing in..." loading state with disabled button, calls POST /auth/login via named "AdminApi" HttpClient, stores JWT via AdminAuthenticationStateProvider.LoginAsync, redirects to /admin on success
- AdminSidebar.razor has 4 nav groups (Recipes, Tenants, Users & Roles, Configuration) with inline Heroicon SVGs, active highlight (bg-indigo-50 + text-indigo-700 + border-l-2 + border-indigo-500), user name/role in footer from JWT claims, logout button calling LogoutAsync, mobile overlay backdrop
- AdminTopbar.razor is mobile-only (md:hidden) with hamburger bars-3 icon and IronMonkey branding
- MainLayout.razor replaces Bootstrap layout with Tailwind-based admin shell using CascadingAuthenticationState wrapper, JS viewport detection for IsMobile, sidebar toggle
- Admin/Index.razor is @page "/admin" @attribute [Authorize] with placeholder stat cards for Phase 10-13 data
- Routes.razor uses AuthorizeRouteView with NotAuthorized redirect to /login, covering all /admin/* routes
- Full solution builds with 0 errors

## Task Commits

Each task was committed atomically:

1. **Task 1: Build Login page** - `6df32f1` (feat)
2. **Task 2: Build admin shell — sidebar, topbar, layout, admin index, auth routes** - `20e6300` (feat)

## Files Created/Modified

- `IronMonkey.Web/Components/Pages/Login.razor` - Login page with JWT flow (created)
- `IronMonkey.Web/Components/Layout/AdminSidebar.razor` - 4-group nav sidebar with Heroicons (created)
- `IronMonkey.Web/Components/Layout/AdminTopbar.razor` - Mobile-only topbar with hamburger (created)
- `IronMonkey.Web/Components/Pages/Admin/Index.razor` - Auth-guarded /admin dashboard (created)
- `IronMonkey.Web/Components/Layout/MainLayout.razor` - Admin shell layout (replaced)
- `IronMonkey.Web/Components/Routes.razor` - AuthorizeRouteView with auth redirect (replaced)

## Decisions Made

- **AuthorizeRouteView with NotAuthorized block**: The NotAuthorized block fires before the page component renders, providing router-level auth enforcement. Nav.NavigateTo("/login") in this block handles both direct navigation to /admin/* URLs and expired sessions.
- **IsMobile via JS eval window.innerWidth**: Single JS call in OnAfterRenderAsync(firstRender: true) sets _isMobile once per layout render. Avoids repeated JS interop on subsequent renders.
- **Admin/Index.razor stat cards use em-dash placeholders**: Stat counts (Recipes, Tenants, Signups, Users) require API calls that belong to Phase 10-13 pages. Em-dash prevents misleading "0" counts while keeping the visual shell complete.

## Deviations from Plan

None — plan executed exactly as written. All 6 files match the plan's specified content. Build succeeds with 0 errors.

## Known Stubs

- `Admin/Index.razor` stat cards display "—" (em-dash) for Recipes, Tenants, Pending Signups, Active Users. These are intentional placeholders per the plan spec: "Phase 10-13 will replace these with real data." These stubs do not prevent the plan's goal (auth shell) from being achieved. Phase 10 (Recipes UI) and Phase 11 (Tenants UI) will wire real counts.

## Self-Check: PASSED

- FOUND: /home/manoj/projects/sandbox/IronMonkey/.claude/worktrees/agent-abed91ee/IronMonkey.Web/Components/Pages/Login.razor
- FOUND: /home/manoj/projects/sandbox/IronMonkey/.claude/worktrees/agent-abed91ee/IronMonkey.Web/Components/Layout/AdminSidebar.razor
- FOUND: /home/manoj/projects/sandbox/IronMonkey/.claude/worktrees/agent-abed91ee/IronMonkey.Web/Components/Layout/AdminTopbar.razor
- FOUND: /home/manoj/projects/sandbox/IronMonkey/.claude/worktrees/agent-abed91ee/IronMonkey.Web/Components/Pages/Admin/Index.razor
- FOUND: commit 6df32f1 (Task 1 — Login page)
- FOUND: commit 20e6300 (Task 2 — admin shell)
- Build verification: `dotnet build IronMonkey.Web/IronMonkey.Web.csproj` exits 0, 0 errors
- Build verification: `dotnet build IronMonkey.sln` exits 0, 0 errors

---
*Phase: 09-ui-foundation-auth*
*Completed: 2026-04-01*
