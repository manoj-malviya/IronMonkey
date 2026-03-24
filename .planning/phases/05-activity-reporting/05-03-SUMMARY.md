---
phase: 05-activity-reporting
plan: 03
subsystem: api
tags: [ef-core, interceptors, activity-log, timeline, pagination, csharp]

requires:
  - phase: 05-02
    provides: ActivityLog entity with JSONB OldValues/NewValues, EF Core migration

provides:
  - ActivityChangeInterceptor: ISaveChangesInterceptor that auto-captures all BaseTenantEntity changes to ActivityLog
  - IActivityTrackingService + ActivityTrackingService: manual note creation API
  - GET /api/leads/{leadId}/activity: paginated, filterable timeline endpoint
  - POST /api/leads/{leadId}/activity/notes: manual note creation endpoint
  - TenantDbContext.TenantId public property for interceptor access
  - TenantDbContextFactory.CreateForTenant overload with IEnumerable<IInterceptor>

affects: [05-04, 05-05, activity-reporting]

tech-stack:
  added: []
  patterns:
    - "ISaveChangesInterceptor: EF Core SaveChanges interceptor registered in DI as scoped service"
    - "Interceptors passed to TenantDbContextFactory via overloaded CreateForTenant with IEnumerable<IInterceptor>"
    - "ActivityChangeInterceptor excludes ActivityLog and OutboxMessage types to prevent recursion"
    - "ResolveLeadId pattern: switch expression mapping entity type to its LeadId"

key-files:
  created:
    - IronMonkey.ApiService/Interceptors/ActivityChangeInterceptor.cs
    - IronMonkey.ApiService/Features/Activity/IActivityTrackingService.cs
    - IronMonkey.ApiService/Features/Activity/ActivityTrackingService.cs
    - IronMonkey.ApiService/Features/Activity/Timeline/GetLeadActivityTimelineEndpoint.cs
    - IronMonkey.ApiService/Features/Activity/Timeline/AddLeadNoteEndpoint.cs
  modified:
    - IronMonkey.Data/TenantDbContext.cs
    - IronMonkey.Data/TenantDbContextFactory.cs
    - IronMonkey.ApiService/ConfigureServices.cs
    - IronMonkey.ApiService/Endpoints.cs

key-decisions:
  - "ActivityChangeInterceptor registered as scoped in DI — IUserContext is scoped per HTTP request, interceptor must also be scoped"
  - "TenantDbContextFactory updated with overloaded CreateForTenant(conn, tenantId, interceptors) — allows passing scoped interceptors at call site without changing singleton factory registration"
  - "User entity has Name property (not FirstName/LastName) — ActorName in ActivityEventDto uses Actor.Name"
  - "ValidationError constructor takes string message — AddLeadNoteEndpoint joins FluentValidation errors with semicolon"
  - "Pre-existing IronMonkey.Web Blazor build errors are out-of-scope and pre-date Phase 5 work"

patterns-established:
  - "Pattern: MapActivityEndpoints() method added to Endpoints.cs following existing MapPipelineEndpoints() convention"
  - "Pattern: Activity interceptor excludes types via static HashSet<Type> ExcludedTypes for O(1) lookup"

requirements-completed: [ACTV-01]

duration: 20min
completed: 2026-03-24
---

# Phase 5 Plan 03: Activity Tracking Interceptor and Timeline API Summary

**EF Core SaveChanges interceptor auto-captures all tenant entity changes to ActivityLog, plus paginated GET /api/leads/{leadId}/activity timeline and POST notes endpoint**

## Performance

- **Duration:** ~20 min
- **Started:** 2026-03-24T17:30:00Z
- **Completed:** 2026-03-24T17:42:32Z
- **Tasks:** 2
- **Files modified:** 9

## Accomplishments
- ActivityChangeInterceptor captures Created/Updated/Deleted events for all BaseTenantEntity types automatically on every SaveChangesAsync call
- GET /api/leads/{leadId}/activity returns paginated (20/page), newest-first events with optional eventTypes filter
- POST /api/leads/{leadId}/activity/notes creates ActivityLog entries with EventType=Note via IActivityTrackingService
- TenantDbContextFactory extended with interceptor overload so scoped interceptors can be injected at DbContext creation time

## Task Commits

1. **Task 1: Implement ActivityChangeInterceptor and IActivityTrackingService** - `f9ff8ce` (feat)
2. **Task 2: Implement GetLeadActivityTimelineEndpoint and AddLeadNoteEndpoint** - `e2f6737` (feat)

## Files Created/Modified
- `IronMonkey.ApiService/Interceptors/ActivityChangeInterceptor.cs` - EF Core interceptor; captures BaseTenantEntity changes, resolves LeadId via switch, excludes ActivityLog/OutboxMessage
- `IronMonkey.ApiService/Features/Activity/IActivityTrackingService.cs` - Interface for manual note creation
- `IronMonkey.ApiService/Features/Activity/ActivityTrackingService.cs` - Implementation that calls ActivityLog.Create with EventType=Note
- `IronMonkey.ApiService/Features/Activity/Timeline/GetLeadActivityTimelineEndpoint.cs` - Paginated timeline endpoint with event type filtering
- `IronMonkey.ApiService/Features/Activity/Timeline/AddLeadNoteEndpoint.cs` - Note creation endpoint with FluentValidation
- `IronMonkey.Data/TenantDbContext.cs` - Added public TenantId property exposing private _tenantId
- `IronMonkey.Data/TenantDbContextFactory.cs` - Added CreateForTenant overload accepting IEnumerable<IInterceptor>
- `IronMonkey.ApiService/ConfigureServices.cs` - Registered ActivityChangeInterceptor and IActivityTrackingService as scoped
- `IronMonkey.ApiService/Endpoints.cs` - Added MapActivityEndpoints() and registered both new endpoints

## Decisions Made
- **TenantDbContextFactory interceptor overload** — The factory is registered as singleton but `ActivityChangeInterceptor` depends on `IUserContext` (scoped). Solution: add `CreateForTenant` overload that accepts `IEnumerable<IInterceptor>`, let call sites resolve scoped interceptors and pass them in. Avoids DI lifetime violations.
- **User.Name vs FirstName/LastName** — The `User` entity uses a single `Name` field; the plan's code referenced `FirstName + LastName` which doesn't exist. Fixed to use `a.Actor.Name`.
- **ValidationError string format** — `ValidationError` constructor only accepts a `string` message. Converted FluentValidation errors to semicolon-joined string.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] IUserContext method name mismatch**
- **Found during:** Task 1 (ActivityChangeInterceptor compilation)
- **Issue:** Plan code used `userContext.GetCurrentUserId()` but actual `IUserContext` interface exposes `UserId` property (established in Phase 1)
- **Fix:** Changed to `userContext.UserId` in `TryGetActorId()`
- **Files modified:** IronMonkey.ApiService/Interceptors/ActivityChangeInterceptor.cs
- **Verification:** dotnet build IronMonkey.ApiService exits 0
- **Committed in:** f9ff8ce (Task 1 commit)

**2. [Rule 1 - Bug] User entity has Name, not FirstName/LastName**
- **Found during:** Task 2 (GetLeadActivityTimelineEndpoint compilation)
- **Issue:** Plan code used `a.Actor.FirstName + " " + a.Actor.LastName` but User entity has a single `Name` field
- **Fix:** Changed to `a.Actor.Name`
- **Files modified:** IronMonkey.ApiService/Features/Activity/Timeline/GetLeadActivityTimelineEndpoint.cs
- **Verification:** dotnet build IronMonkey.ApiService exits 0
- **Committed in:** e2f6737 (Task 2 commit)

**3. [Rule 1 - Bug] ValidationError constructor takes string, not List<ValidationFailure>**
- **Found during:** Task 2 (AddLeadNoteEndpoint implementation review)
- **Issue:** Plan code used `new ValidationError(validationResult.Errors)` but ValidationError only has string constructor
- **Fix:** Changed to `new ValidationError(string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)))`
- **Files modified:** IronMonkey.ApiService/Features/Activity/Timeline/AddLeadNoteEndpoint.cs
- **Verification:** dotnet build IronMonkey.ApiService exits 0
- **Committed in:** e2f6737 (Task 2 commit)

**4. [Rule 2 - Missing Critical] TenantDbContextFactory interceptor support**
- **Found during:** Task 1 (registering ActivityChangeInterceptor)
- **Issue:** `TenantDbContextFactory.CreateForTenant` had no way to inject interceptors. Factory is a singleton but interceptor is scoped — needed an overload accepting IEnumerable<IInterceptor> to allow call sites to pass resolved interceptors
- **Fix:** Added overloaded `CreateForTenant(connectionString, tenantId, interceptors)` to both interface and implementation
- **Files modified:** IronMonkey.Data/TenantDbContextFactory.cs
- **Verification:** dotnet build IronMonkey.ApiService exits 0
- **Committed in:** f9ff8ce (Task 1 commit)

---

**Total deviations:** 4 auto-fixed (3 Rule 1 bugs, 1 Rule 2 missing critical)
**Impact on plan:** All fixes were required for compilation and correctness. No scope creep.

## Issues Encountered
- Worktree was on older branch without Phase 4-5 entities. Resolved by merging `phase4` branch into the worktree before starting implementation.
- Pre-existing IronMonkey.Web Blazor compilation errors (ApiClient.GetAsync/PostAsync not defined) exist from early project commits and are out of scope for this plan. IronMonkey.ApiService builds successfully with 0 errors.

## Known Stubs
- `AddLeadNoteEndpoint.Response(Guid.NewGuid())` — returns a randomly generated Guid instead of the actual ActivityLog.Id created by AddNoteAsync. This is an acknowledged limitation in the plan ("for simplicity, the endpoint confirms the note was created"). To fix: update `IActivityTrackingService.AddNoteAsync` to return the created `Guid`. Tracked for future plans.

## Next Phase Readiness
- Activity interception is wired up. Any call to SaveChangesAsync on TenantDbContext will now create ActivityLog entries (when interceptor is passed to CreateForTenant).
- Timeline endpoint is ready for frontend consumption.
- Plans 05-04 and 05-05 (dashboard/reporting endpoints) can build on ActivityLog data directly.

---
*Phase: 05-activity-reporting*
*Completed: 2026-03-24*
