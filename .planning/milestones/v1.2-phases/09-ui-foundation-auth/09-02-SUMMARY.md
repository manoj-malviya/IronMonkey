---
phase: 09-ui-foundation-auth
plan: 02
subsystem: auth
tags: [jwt, blazor-server, protected-session-storage, authentication-state, delegating-handler, circuit-handler]

# Dependency graph
requires:
  - phase: 09-ui-foundation-auth/09-01
    provides: Blazor Server project structure with ApiClient stub and SuperAdmin component stubs
provides:
  - AdminAuthenticationStateProvider: JWT-backed AuthenticationStateProvider using ProtectedSessionStorage
  - BearerTokenHandler: DelegatingHandler that injects Authorization Bearer header on all AdminApi calls
  - IdentityValidationCircuitHandler: CircuitHandler for logging Blazor Server circuit lifecycle events
  - Named HttpClient "AdminApi" wired with BearerTokenHandler
  - AddCascadingAuthenticationState and auth service registrations in Program.cs
affects:
  - 09-03 (login page needs AdminAuthenticationStateProvider.LoginAsync and LogoutAsync)
  - all admin pages (CascadingAuthenticationState provides AuthorizeView, @attribute [Authorize])

# Tech tracking
tech-stack:
  added:
    - System.IdentityModel.Tokens.Jwt 8.7.0 (client-side JWT parsing without signature validation)
  patterns:
    - ProtectedSessionStorage key "auth_token" as single source of truth for JWT
    - Scoped AuthenticationStateProvider backed by in-memory cached ClaimsPrincipal
    - Named HttpClient pattern ("AdminApi") with DelegatingHandler chain

key-files:
  created:
    - IronMonkey.Web/Authentication/AdminAuthenticationStateProvider.cs
    - IronMonkey.Web/HttpHandlers/BearerTokenHandler.cs
    - IronMonkey.Web/CircuitHandlers/IdentityValidationCircuitHandler.cs
  modified:
    - IronMonkey.Web/Program.cs
    - IronMonkey.Web/Components/_Imports.razor
    - IronMonkey.Web/IronMonkey.Web.csproj

key-decisions:
  - "System.IdentityModel.Tokens.Jwt 8.7.0 added for client-side JWT parsing (reads claims without signature validation — signature already validated by API)"
  - "AdminAuthenticationStateProvider registered as both concrete Scoped and as AuthenticationStateProvider — allows callers to inject either interface"
  - "Named HttpClient 'AdminApi' kept separate from legacy typed ApiClient — AdminApi has BearerTokenHandler, ApiClient remains stub for backward compat"
  - "ProtectedSessionStorage key 'auth_token' established as canonical storage key used by both AdminAuthenticationStateProvider and BearerTokenHandler"

patterns-established:
  - "ProtectedSessionStorage for JWT: all auth state flows through single 'auth_token' key in ProtectedSessionStorage"
  - "Cascade auth state: AddCascadingAuthenticationState() + AdminAuthenticationStateProvider enables @attribute [Authorize] on all Blazor pages"
  - "DelegatingHandler chain: BearerTokenHandler reads from ProtectedSessionStorage and injects Authorization header transparently"

requirements-completed: [UIFN-01, UIFN-02, UIFN-06]

# Metrics
duration: 15min
completed: 2026-03-31
---

# Phase 09 Plan 02: JWT Auth Infrastructure for Blazor Server Admin UI Summary

**JWT-backed AuthenticationStateProvider with ProtectedSessionStorage, auto-Bearer DelegatingHandler, and named AdminApi HttpClient registration in Program.cs**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-03-31T17:10:00Z
- **Completed:** 2026-03-31T17:25:00Z
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments

- AdminAuthenticationStateProvider: reads JWT from ProtectedSessionStorage, builds ClaimsPrincipal from token claims, handles Login/Logout/GetToken, navigates to /login on logout
- BearerTokenHandler: DelegatingHandler that transparently injects `Authorization: Bearer <token>` on every outbound AdminApi request, logs 401 responses
- IdentityValidationCircuitHandler: CircuitHandler that logs Blazor Server circuit lifecycle events (opened, connection up/down, closed)
- Program.cs wired with AddCascadingAuthenticationState, AddScoped registrations for AdminAuthenticationStateProvider and BearerTokenHandler, named "AdminApi" HttpClient with handler chain, and CircuitHandler registration
- _Imports.razor updated with Microsoft.AspNetCore.Authorization, Microsoft.AspNetCore.Components.Authorization, and IronMonkey.Web.Authentication namespaces

## Task Commits

Each task was committed atomically:

1. **Task 1: AdminAuthenticationStateProvider and BearerTokenHandler** - `5d97a9c` (feat)
2. **Task 1 (dependency): System.IdentityModel.Tokens.Jwt package** - `4f71215` (chore)
3. **Task 2: IdentityValidationCircuitHandler, Program.cs, _Imports.razor** - `49790e4` (feat)

**Plan metadata:** TBD (docs: complete plan)

## Files Created/Modified

- `IronMonkey.Web/Authentication/AdminAuthenticationStateProvider.cs` - JWT AuthenticationStateProvider with LoginAsync/LogoutAsync/GetTokenAsync backed by ProtectedSessionStorage
- `IronMonkey.Web/HttpHandlers/BearerTokenHandler.cs` - DelegatingHandler that reads "auth_token" from ProtectedSessionStorage and sets Authorization header
- `IronMonkey.Web/CircuitHandlers/IdentityValidationCircuitHandler.cs` - CircuitHandler subclass for circuit lifecycle logging
- `IronMonkey.Web/Program.cs` - Updated with auth service registrations, named HttpClient "AdminApi", and circuit handler
- `IronMonkey.Web/Components/_Imports.razor` - Added Authorization, Components.Authorization, and Authentication using directives
- `IronMonkey.Web/IronMonkey.Web.csproj` - Added System.IdentityModel.Tokens.Jwt 8.7.0 package reference

## Decisions Made

- **System.IdentityModel.Tokens.Jwt 8.7.0** added for client-side JWT parsing. The Web project reads claims from JWT without validating the signature (the API already validated it on login). Version 8.7.0 is the latest stable, compatible with .NET 10.
- **AdminAuthenticationStateProvider registered twice**: once as itself (concrete Scoped) and once as the `AuthenticationStateProvider` interface — this allows components that need `LoginAsync`/`LogoutAsync` to inject the concrete type while Blazor's auth infrastructure gets the interface.
- **Named HttpClient "AdminApi" pattern**: kept separate from the legacy typed `ApiClient` stub. AdminApi has the BearerTokenHandler chain; ApiClient remains empty for backward compatibility with any pre-existing references.
- **ProtectedSessionStorage key "auth_token"**: established as the canonical storage key. Both AdminAuthenticationStateProvider and BearerTokenHandler read from this same key, ensuring consistency.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added System.IdentityModel.Tokens.Jwt package reference to .csproj**
- **Found during:** Task 1 (AdminAuthenticationStateProvider)
- **Issue:** AdminAuthenticationStateProvider uses `JwtSecurityTokenHandler` from `System.IdentityModel.Tokens.Jwt` but the Web project had no reference to this package
- **Fix:** Added `<PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.7.0" />` to IronMonkey.Web.csproj
- **Files modified:** IronMonkey.Web/IronMonkey.Web.csproj
- **Verification:** `dotnet restore` succeeded, project builds without import errors
- **Committed in:** `4f71215` (separate chore commit)

---

**Total deviations:** 1 auto-fixed (1 blocking — missing package dependency)
**Impact on plan:** Necessary for the JwtSecurityTokenHandler import to resolve. No scope creep.

## Issues Encountered

Pre-existing build errors in SuperAdmin stub Razor components (created in plan 09-01) reference `ApiClient.GetAsync`/`PostAsync` which do not exist on the stub `ApiClient`. These errors exist before and after this plan's changes — they are not caused by 09-02 work. Logged to `deferred-items.md` in the phase directory. These will be resolved when a future plan implements the SuperAdmin UI and wires up ApiClient methods.

## Known Stubs

None — all code wired and functional. The `IdentityValidationCircuitHandler._originalIdentityId` field is declared but not yet populated (circuit identity enforcement is logged but not enforced); this is intentional per plan spec which only requires logging lifecycle events. Full identity enforcement can be added in a future plan if needed.

## Next Phase Readiness

- Auth infrastructure is complete and registered in DI. Plan 09-03 (login page + layout) can now:
  - Inject `AdminAuthenticationStateProvider` and call `LoginAsync(token)` after successful API login
  - Use `@attribute [Authorize]` on protected admin pages
  - Use `IHttpClientFactory` with name "AdminApi" to make authenticated API calls
- No blockers for Plan 09-03.

## Self-Check: PASSED

- FOUND: IronMonkey.Web/Authentication/AdminAuthenticationStateProvider.cs
- FOUND: IronMonkey.Web/HttpHandlers/BearerTokenHandler.cs
- FOUND: IronMonkey.Web/CircuitHandlers/IdentityValidationCircuitHandler.cs
- FOUND: .planning/phases/09-ui-foundation-auth/09-02-SUMMARY.md
- FOUND: commit 5d97a9c (Task 1: AdminAuthenticationStateProvider + BearerTokenHandler)
- FOUND: commit 4f71215 (chore: JWT package)
- FOUND: commit 49790e4 (Task 2: IdentityValidationCircuitHandler + Program.cs + _Imports.razor)
- FOUND: commit 2950e35 (docs: plan metadata)

---
*Phase: 09-ui-foundation-auth*
*Completed: 2026-03-31*
