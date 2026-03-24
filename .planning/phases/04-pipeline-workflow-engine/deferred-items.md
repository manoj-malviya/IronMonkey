# Deferred Items — Phase 04

## Pre-existing Web Project Build Errors

**Discovered during:** 04-05 Task 2 (full solution build)

**Issue:** `IronMonkey.Web` has pre-existing compilation errors in Blazor components:
- `ApiClient` static class missing `GetAsync`/`PostAsync` methods (referenced in ListRoles.razor, ListPermissions.razor, CreatePermission.razor, CreateRoleAndAssignPermission.razor)
- `JSRuntime` used without injection via `[Inject]` attribute (static call to instance method)
- `CreatePermission.razor` had a naming collision (method same as component class) — this was fixed inline as it caused the first `dotnet build IronMonkey.sln` failure

**Impact:** `dotnet build IronMonkey.sln` does not exit 0. `dotnet build IronMonkey.ApiService` exits 0.

**Action Required:** Fix `IronMonkey.Web/Components/SuperAdmin/` components to use proper Blazor injection pattern and the correct `ApiClient` methods. Defer to a future Web UI phase.
