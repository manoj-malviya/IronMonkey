---
phase: 01-multi-tenancy-foundation
plan: 03
subsystem: auth
tags: [jwt, bcrypt, tenancy, efcore, postgresql, claims]

# Dependency graph
requires:
  - phase: 01-multi-tenancy-foundation/01-02
    provides: CentralDbContext with Tenant entity (DatabaseConnectionString, IsProvisioned), TenantDbContext with Users/Roles, TenantDbContextFactory
provides:
  - LoggedInUser record with TenantId (5th parameter)
  - tenant_id JWT claim embedded in every generated token
  - ITenantService interface and TenantService implementation
  - IUserContext.TenantId property (and UserContext impl)
  - LoginEndpoint POST /auth/login with email-to-tenant central index lookup
  - UserTenantIndex entity + central DB table for email→tenant mapping
  - EF migration AddUserTenantIndex
  - CrossTenantSecurityTests (2 tests verifying JWT enforcement)
affects:
  - 01-multi-tenancy-foundation/01-04 (provisioning)
  - all future tenant API endpoints (depend on ITenantService to resolve connection string)

# Tech tracking
tech-stack:
  added:
    - BCrypt.Net-Next 4.0.3 (password hashing/verification in LoginEndpoint)
    - Microsoft.EntityFrameworkCore.InMemory 10.0.5 (unit test DbContext)
  patterns:
    - Email-to-tenant central index pattern (UserTenantIndex table in central DB)
    - TenantService reads tenant_id from IHttpContextAccessor JWT claims
    - IEndpoint pattern for minimal API endpoints (LoginEndpoint)

key-files:
  created:
    - IronMonkey.Common/Auth/LoggedInUser.cs (extended with TenantId)
    - IronMonkey.ApiService/Common/Auth/ITenantService.cs
    - IronMonkey.ApiService/Common/Auth/TenantService.cs
    - IronMonkey.ApiService/Authentication/Endpoints/LoginEndpoint.cs
    - IronMonkey.Data/Entities/UserTenantIndex.cs
    - IronMonkey.Data/Migrations/Central/20260320101137_AddUserTenantIndex.cs
    - IronMonkey.ApiService/AssemblyInfo.cs (InternalsVisibleTo for tests)
  modified:
    - IronMonkey.Common/Auth/Jwt.cs (tenant_id claim in GenerateToken and GetUserFromToken)
    - IronMonkey.ApiService/Common/Extensions/ClaimsPrincipalExtensions.cs (GetTenantId extension)
    - IronMonkey.ApiService/Common/Auth/IUserContext.cs (TenantId property added)
    - IronMonkey.ApiService/Common/Auth/UserContext.cs (TenantId impl)
    - IronMonkey.ApiService/ConfigureServices.cs (AddScoped<ITenantService, TenantService>)
    - IronMonkey.ApiService/ConfigureApp.cs (EnsureDatabaseCreated uses CentralDbContext)
    - IronMonkey.ApiService/Endpoints.cs (LoginEndpoint registered)
    - IronMonkey.Data/CentralDbContext.cs (UserTenantIndex DbSet + OnModelCreating config)
    - IronMonkey.Tests/Integration/CrossTenantSecurityTests.cs (stubs replaced with real tests)

key-decisions:
  - "UserTenantIndex central table: email→tenantId mapping in central DB enables O(1) login tenant lookup without scanning all tenant DBs"
  - "TenantService is internal sealed — InternalsVisibleTo test assembly access added via AssemblyInfo.cs"
  - "LoginEndpoint performs 3-step flow: central index lookup -> tenant provisioning check -> tenant DB password BCrypt verify"
  - "ConfigureApp.cs EnsureDatabaseCreated migrates CentralDbContext (replaced obsolete AppDbContext)"

patterns-established:
  - "JWT tenant_id claim: every LoggedInUser carries TenantId, GenerateToken embeds it, GetTenantId() extension reads it"
  - "ITenantService.GetCurrentTenantId() throws ApplicationException if tenant_id claim missing — fail-fast security boundary"
  - "All protected endpoints resolve tenant via ITenantService.GetConnectionStringAsync() before opening TenantDbContext"

requirements-completed: [TNCY-02]

# Metrics
duration: 10min
completed: 2026-03-20
---

# Phase 1 Plan 3: JWT Tenant Pipeline Summary

**tenant_id JWT claim embedded in login, TenantService resolves per-tenant connection string via central DB index, LoginEndpoint performs 3-step email→tenant→password flow using BCrypt**

## Performance

- **Duration:** 10 min
- **Started:** 2026-03-20T10:06:29Z
- **Completed:** 2026-03-20T10:16:09Z
- **Tasks:** 3
- **Files modified:** 15

## Accomplishments
- LoggedInUser record extended with TenantId; Jwt.GenerateToken embeds `tenant_id` claim; GetUserFromToken reads it back
- ITenantService/TenantService created — scoped service reads tenant_id from JWT, looks up Tenant.DatabaseConnectionString from CentralDbContext
- LoginEndpoint POST /auth/login: central email index lookup (UserTenantIndex) → provisioning check → BCrypt password verify → JWT with TenantId
- IUserContext.TenantId added to interface and UserContext implementation
- UserTenantIndex entity + EF migration + CentralDbContext configuration for email-to-tenant mapping
- CrossTenantSecurityTests: 2 real unit tests replacing Wave-0 Skip stubs — verify tenant_id enforcement

## Task Commits

Each task was committed atomically:

1. **Task 1: Extend JWT pipeline** - `2e7c23c` (feat)
2. **Task 2: ITenantService, TenantService, LoginEndpoint, DI wiring** - `bc86d25` (feat)
3. **Task 3: CrossTenantSecurityTests** - `f424a90` (test)

## Files Created/Modified
- `IronMonkey.Common/Auth/LoggedInUser.cs` - Added TenantId as 5th parameter to record
- `IronMonkey.Common/Auth/Jwt.cs` - tenant_id claim in GenerateToken; read back in GetUserFromToken
- `IronMonkey.ApiService/Common/Extensions/ClaimsPrincipalExtensions.cs` - GetTenantId() extension method
- `IronMonkey.ApiService/Common/Auth/ITenantService.cs` - Interface: GetCurrentTenantId() + GetConnectionStringAsync()
- `IronMonkey.ApiService/Common/Auth/TenantService.cs` - Scoped impl reading tenant_id claim, querying CentralDbContext
- `IronMonkey.ApiService/Common/Auth/IUserContext.cs` - TenantId property added to interface
- `IronMonkey.ApiService/Common/Auth/UserContext.cs` - TenantId property implemented via GetTenantId()
- `IronMonkey.ApiService/Authentication/Endpoints/LoginEndpoint.cs` - POST /auth/login with 3-step flow
- `IronMonkey.Data/Entities/UserTenantIndex.cs` - Central email→tenant mapping entity
- `IronMonkey.Data/CentralDbContext.cs` - UserTenantIndex DbSet + model configuration with email index
- `IronMonkey.Data/Migrations/Central/20260320101137_AddUserTenantIndex.cs` - EF migration
- `IronMonkey.ApiService/ConfigureServices.cs` - ITenantService registered as Scoped
- `IronMonkey.ApiService/ConfigureApp.cs` - EnsureDatabaseCreated uses CentralDbContext
- `IronMonkey.ApiService/Endpoints.cs` - LoginEndpoint registered in auth group
- `IronMonkey.ApiService/AssemblyInfo.cs` - InternalsVisibleTo("IronMonkey.Tests")
- `IronMonkey.Tests/Integration/CrossTenantSecurityTests.cs` - 2 real tests replacing Skip stubs
- `IronMonkey.Tests/IronMonkey.Tests.csproj` - Added EF InMemory package

## Decisions Made
- **UserTenantIndex central table**: email→tenantId mapping in central DB enables O(1) login tenant lookup — no full tenant DB scan needed at login time. Populated when users are created in tenant DBs.
- **TenantService internal sealed**: Added `InternalsVisibleTo("IronMonkey.Tests")` via AssemblyInfo.cs rather than making it public, preserving encapsulation while enabling test access.
- **BCrypt.Net-Next 4.0.3**: Added for password verification in LoginEndpoint; consistent with how User.Password should be stored (hashed).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added IronMonkey.ApiService.Common.Extensions using to LoginEndpoint**
- **Found during:** Task 2 (LoginEndpoint compilation)
- **Issue:** `WithRequestValidation<T>` extension method not found — missing using directive
- **Fix:** Added `using IronMonkey.ApiService.Common.Extensions;` to LoginEndpoint.cs
- **Files modified:** IronMonkey.ApiService/Authentication/Endpoints/LoginEndpoint.cs
- **Verification:** `dotnet build` passed with 0 errors
- **Committed in:** bc86d25 (Task 2 commit)

**2. [Rule 3 - Blocking] Added InternalsVisibleTo for test access to TenantService**
- **Found during:** Task 3 (CrossTenantSecurityTests compilation)
- **Issue:** TenantService is `internal sealed` — test project cannot access it without visibility attribute
- **Fix:** Created IronMonkey.ApiService/AssemblyInfo.cs with `[assembly: InternalsVisibleTo("IronMonkey.Tests")]`
- **Files modified:** IronMonkey.ApiService/AssemblyInfo.cs
- **Verification:** Tests compiled and passed
- **Committed in:** f424a90 (Task 3 commit)

**3. [Rule 3 - Blocking] Added Microsoft.EntityFrameworkCore.InMemory to test project**
- **Found during:** Task 3 (CrossTenantSecurityTests needs InMemory DB for CentralDbContext)
- **Issue:** InMemory provider not available in test project; needed to construct CentralDbContext without real PostgreSQL
- **Fix:** `dotnet add IronMonkey.Tests package Microsoft.EntityFrameworkCore.InMemory --version 10.0.5`
- **Files modified:** IronMonkey.Tests/IronMonkey.Tests.csproj
- **Verification:** Tests compiled and passed
- **Committed in:** f424a90 (Task 3 commit)

---

**Total deviations:** 3 auto-fixed (all Rule 3 - blocking compilation issues)
**Impact on plan:** All fixes necessary for compilation. No scope creep.

## Issues Encountered
- None beyond the deviations documented above.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- JWT pipeline is complete: every authenticated request carries TenantId, ITenantService resolves connection string
- LoginEndpoint requires UserTenantIndex to be populated when users are created — Plan 04 (provisioning) must populate this index when tenant users are created
- TenantDbContext.Users includes Roles via HasMany/WithMany join — Include(u => u.Roles) works correctly in LoginEndpoint

---
*Phase: 01-multi-tenancy-foundation*
*Completed: 2026-03-20*
