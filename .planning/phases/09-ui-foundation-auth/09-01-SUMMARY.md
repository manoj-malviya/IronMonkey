---
phase: 09-ui-foundation-auth
plan: 01
subsystem: ui
tags: [tailwind, blazor, aspire, css, msbuild]

# Dependency graph
requires:
  - phase: 01-multi-tenancy-foundation
    provides: Aspire AppHost orchestration with PostgreSQL and ApiService
provides:
  - Tailwind CSS v4.2.2 standalone CLI integrated into MSBuild pipeline
  - wwwroot/css/output.css generated automatically on every dotnet build
  - App.razor using Tailwind output.css (Bootstrap removed)
  - IronMonkey.Web registered in Aspire AppHost as webfrontend with apiService reference
affects:
  - 09-02-ui-foundation-auth (JWT auth infra depends on Tailwind CSS being available)
  - 09-03-ui-foundation-auth (Login page and sidebar use Tailwind utility classes)
  - All Phase 10-13 UI plans (Tailwind is the CSS foundation)

# Tech tracking
tech-stack:
  added:
    - tailwindcss v4.2.2 (standalone CLI binary at ~/.local/bin/tailwindcss)
    - Aspire.Hosting.PostgreSQL 9.1.0 (added to AppHost for AddPostgres extension method)
  patterns:
    - MSBuild BeforeTargets="ResolveStaticWebAssets;Build" pattern for Tailwind CLI integration
    - wwwroot/css/.gitignore for generated output exclusion

key-files:
  created:
    - IronMonkey.Web/wwwroot/css/input.css
    - IronMonkey.Web/wwwroot/css/.gitignore
  modified:
    - IronMonkey.Web/IronMonkey.Web.csproj
    - IronMonkey.Web/Components/App.razor
    - IronMonkey.AppHost/AppHost.cs
    - IronMonkey.AppHost/IronMonkey.AppHost.csproj
    - IronMonkey.Web/ApiClient.cs
    - IronMonkey.Web/Components/SuperAdmin/*.razor (4 files)
    - IronMonkey.ApiService/Endpoints.cs

key-decisions:
  - "Tailwind v4 BeforeTargets includes both ResolveStaticWebAssets and Build to ensure output.css exists before static asset fingerprinting checks run"
  - "Tailwind v4 standalone CLI installed at ~/.local/bin/tailwindcss (no npm required)"
  - "Aspire.Hosting.PostgreSQL 9.1.0 added to AppHost for AddPostgres extension (was pre-existing missing dependency)"
  - "ApiClient.GetAsync<T> and PostAsync added as instance methods to fix SuperAdmin component compilation (pre-existing bug)"

patterns-established:
  - "Tailwind MSBuild: BeforeTargets=ResolveStaticWebAssets ensures CSS file exists before Aspire static web asset validation"
  - "Tailwind input.css uses @import tailwindcss (v4 syntax, no config file needed, auto-scans .razor files)"

requirements-completed: [UIFN-05]

# Metrics
duration: 35min
completed: 2026-03-31
---

# Phase 9 Plan 01: Tailwind CSS MSBuild Integration + AppHost Wire-up Summary

**Tailwind CSS v4.2.2 standalone CLI integrated into MSBuild pipeline via BeforeTargets hook, Bootstrap removed from App.razor, and IronMonkey.Web registered as webfrontend in Aspire AppHost orchestration**

## Performance

- **Duration:** 35 min
- **Started:** 2026-03-31T17:18:00Z
- **Completed:** 2026-03-31T17:53:00Z
- **Tasks:** 2
- **Files modified:** 11

## Accomplishments
- Tailwind CSS v4.2.2 standalone CLI compiles styles automatically on every `dotnet build` via MSBuild `BuildTailwindCSS` target
- Bootstrap removed from App.razor; `css/output.css` (18.7 KB minified) now linked instead
- IronMonkey.Web registered in Aspire AppHost with `WithReference(apiService)` and `WithExternalHttpEndpoints()` so the web frontend appears in the Aspire dashboard
- Full solution builds with 0 errors and 0 warnings

## Task Commits

Each task was committed atomically:

1. **Task 1: Create Tailwind input.css and MSBuild target** - `7e55ac1` (feat)
2. **Task 2: Replace Bootstrap with Tailwind in App.razor and wire Web to AppHost** - `a36ba53` (feat)

## Files Created/Modified
- `IronMonkey.Web/wwwroot/css/input.css` - Tailwind v4 entry point with `@import "tailwindcss"`
- `IronMonkey.Web/wwwroot/css/.gitignore` - Excludes generated output.css from git
- `IronMonkey.Web/IronMonkey.Web.csproj` - BuildTailwindCSS MSBuild target added
- `IronMonkey.Web/Components/App.razor` - Bootstrap removed, Tailwind output.css linked
- `IronMonkey.AppHost/AppHost.cs` - webFrontend project added with Aspire references
- `IronMonkey.AppHost/IronMonkey.AppHost.csproj` - Aspire.Hosting.PostgreSQL 9.1.0 added
- `IronMonkey.Web/ApiClient.cs` - GetAsync<T> and PostAsync instance methods added
- `IronMonkey.Web/Components/SuperAdmin/ListRoles.razor` - @inject directives added
- `IronMonkey.Web/Components/SuperAdmin/ListPermissions.razor` - @inject directives added
- `IronMonkey.Web/Components/SuperAdmin/CreatePermission.razor` - @inject directives added
- `IronMonkey.Web/Components/SuperAdmin/CreateRoleAndAssignPermission.razor` - @inject directives added
- `IronMonkey.ApiService/Endpoints.cs` - Duplicate MapIngestionEndpoints method removed

## Decisions Made
- BeforeTargets includes `ResolveStaticWebAssets` in addition to `Build` — this is required because .NET SDK validates static web asset files before the C# build phase; without it the first-ever build fails because output.css doesn't exist yet
- Tailwind v4 standalone CLI downloaded and installed at `~/.local/bin/tailwindcss` (no npm/node required)
- Aspire.Hosting.PostgreSQL 9.1.0 was missing from the AppHost project (pre-existing gap) — added as part of fixing the solution build

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] MSBuild target order: BeforeTargets must include ResolveStaticWebAssets**
- **Found during:** Task 1 (Create Tailwind input.css and MSBuild target)
- **Issue:** Plan specified `BeforeTargets="Build"` but .NET SDK's `DefineStaticWebAssets` task runs before `Build` and validates that any `<Content Include>` items exist on disk. First build fails with `System.InvalidOperationException: No file exists for the asset at output.css`
- **Fix:** Changed to `BeforeTargets="ResolveStaticWebAssets;Build"` and removed the `<Content>` ItemGroup (not needed for Tailwind output)
- **Files modified:** IronMonkey.Web/IronMonkey.Web.csproj
- **Verification:** `dotnet build IronMonkey.Web/IronMonkey.Web.csproj` exits 0
- **Committed in:** 7e55ac1 (Task 1 commit)

**2. [Rule 3 - Blocking] Pre-existing SuperAdmin razor components had missing injection (build errors)**
- **Found during:** Task 1 (verifying web project builds)
- **Issue:** Four SuperAdmin components called `ApiClient.GetAsync<T>()` and `ApiClient.PostAsync()` as if they were static methods; `ApiClient` class was empty; no `@inject` directives present. Build produced 10 errors.
- **Fix:** Added `GetAsync<T>` and `PostAsync` instance methods to `ApiClient.cs`; added `@inject ApiClient ApiClient` and `@inject IJSRuntime JSRuntime` to all four components; fixed nullable reference warnings
- **Files modified:** IronMonkey.Web/ApiClient.cs, all 4 SuperAdmin .razor files
- **Verification:** `dotnet build IronMonkey.Web/IronMonkey.Web.csproj` exits 0, 0 warnings
- **Committed in:** 7e55ac1 (Task 1 commit)

**3. [Rule 1 - Bug] Duplicate MapIngestionEndpoints method in Endpoints.cs**
- **Found during:** Task 2 (running full solution build)
- **Issue:** `Endpoints.cs` had `MapIngestionEndpoints()` defined twice (lines 137 and 182) — identical methods — causing CS0111 compile error
- **Fix:** Removed the second duplicate definition at line 182
- **Files modified:** IronMonkey.ApiService/Endpoints.cs
- **Verification:** `dotnet build IronMonkey.sln` exits 0, 0 errors
- **Committed in:** a36ba53 (Task 2 commit)

**4. [Rule 3 - Blocking] Missing Aspire.Hosting.PostgreSQL package in AppHost**
- **Found during:** Task 2 (running full solution build)
- **Issue:** AppHost used `builder.AddPostgres()` but `Aspire.Hosting.PostgreSQL` package was not referenced, causing CS1061 error
- **Fix:** Added `Aspire.Hosting.PostgreSQL 9.1.0` package reference to AppHost.csproj
- **Files modified:** IronMonkey.AppHost/IronMonkey.AppHost.csproj
- **Verification:** AppHost builds successfully; full solution builds with 0 errors
- **Committed in:** a36ba53 (Task 2 commit)

---

**Total deviations:** 4 auto-fixed (2 blocking, 1 bug, 1 blocking)
**Impact on plan:** All auto-fixes necessary for correctness and build success. No scope creep beyond what was strictly needed for the build to pass.

## Issues Encountered
- The Tailwind CSS standalone CLI was not pre-installed on the machine. Downloaded and installed v4.2.2 from GitHub releases to `~/.local/bin/tailwindcss`.
- `git stash` operations during verification caused files to be reverted — needed to re-apply changes twice. Used `git stash drop` to clean up and re-applied all changes manually.

## Known Stubs
None — this plan creates infrastructure (MSBuild target, CSS pipeline, AppHost wiring) with no UI stubs.

## User Setup Required
- **Tailwind CSS standalone CLI installed** at `~/.local/bin/tailwindcss` (v4.2.2)
- This satisfies the `user_setup` prerequisite from the plan frontmatter
- Other developers must install the Tailwind CLI before building: `curl -sL https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64 -o /usr/local/bin/tailwindcss && chmod +x /usr/local/bin/tailwindcss`

## Next Phase Readiness
- Plan 09-02 (JWT auth infrastructure) can proceed immediately — Tailwind CSS is available
- Plan 09-03 (Login page, admin shell, sidebar) can use Tailwind utility classes from the compiled output.css
- Any plan needing Tailwind classes will find them automatically compiled during build

---
*Phase: 09-ui-foundation-auth*
*Completed: 2026-03-31*
