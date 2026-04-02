# Deferred Items — Phase 09 UI Foundation Auth

## Pre-existing Build Errors (Out of Scope)

### SuperAdmin Component Stub Errors

**Discovered during:** 09-02 Task 1 execution
**Status:** Pre-existing — existed before 09-02 changes

The following SuperAdmin Razor components reference `ApiClient.GetAsync` and `ApiClient.PostAsync` which don't exist on the stub `ApiClient` class:

- `IronMonkey.Web/Components/SuperAdmin/ListRoles.razor` — calls `ApiClient.GetAsync`
- `IronMonkey.Web/Components/SuperAdmin/ListPermissions.razor` — calls `ApiClient.GetAsync`
- `IronMonkey.Web/Components/SuperAdmin/CreateRoleAndAssignPermission.razor` — calls `ApiClient.GetAsync`, `ApiClient.PostAsync`
- `IronMonkey.Web/Components/SuperAdmin/CreatePermission.razor` — calls `ApiClient.PostAsync`

These were created as stubs in plan 09-01 and are expected to be wired in a later plan that implements the SuperAdmin UI. Not caused by 09-02 changes.
