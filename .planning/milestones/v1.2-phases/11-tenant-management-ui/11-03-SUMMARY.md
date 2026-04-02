---
phase: 11-tenant-management-ui
plan: 03
subsystem: web-ui + api
tags: [blazor, modal, approve, reject, provision, integration-tests]
dependency_graph:
  requires: [11-01, 11-02]
  provides: [TNUI-03, TNUI-04]
  affects: [TenantManagement.razor, ApproveTenantEndpoint, RejectTenantEndpoint]
tech_stack:
  added: []
  patterns: [two-step-api-chain, optimistic-ui-update, guard-flag-async, internals-visible-to]
key_files:
  created:
    - IronMonkey.Tests/Integration/TenantManagement/ApproveAndProvisionTests.cs
    - IronMonkey.Tests/Integration/TenantManagement/RejectSignupRequestTests.cs
  modified:
    - IronMonkey.Web/Components/Pages/Admin/Tenants/TenantManagement.razor
    - IronMonkey.ApiService/Authentication/Endpoints/ApproveTenantEndpoint.cs
    - IronMonkey.ApiService/Authentication/Endpoints/RejectTenantEndpoint.cs
    - IronMonkey.ApiService/IronMonkey.ApiService.csproj
decisions:
  - "Handle methods on ApproveTenantEndpoint and RejectTenantEndpoint changed from private to internal, with InternalsVisibleTo allowing direct invocation in integration tests"
  - "Two-step approve+provision chain: approve first then provision, with partial failure showing retry button (D-17)"
  - "Reject modal requires front-end validation before sending — empty reason shows inline error and does not submit"
metrics:
  duration: ~8 minutes
  completed: 2026-04-01T15:36:35Z
  tasks_completed: 2
  files_changed: 6
---

# Phase 11 Plan 03: Approve/Reject Modals and Integration Tests Summary

**One-liner:** Approve modal with two-step API chain (approve then provision), reject modal with required-reason validation, partial provision failure retry, and integration tests via internal Handle methods.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Integration tests for approve and reject flows | 1ab74f3 | ApproveAndProvisionTests.cs, RejectSignupRequestTests.cs, ApproveTenantEndpoint.cs, RejectTenantEndpoint.cs, IronMonkey.ApiService.csproj |
| 2 | Approve/Reject modals and action handlers | 4931563 | TenantManagement.razor |

## What Was Built

### TenantManagement.razor
- **Approve modal** — fixed-position backdrop overlay with optional approval note textarea, loading state on Confirm button (`_isApproving`), Cancel resets state without side effects
- **Reject modal** — required rejection reason textarea, inline validation error if empty, loading state (`_isRejecting`)
- **HandleApproveAsync** — two-step: POST /admin/signup/{id}/approve then POST /admin/tenants/{id}/provision; partial failure leaves row as Approved with a Provision retry button (D-17)
- **HandleRejectAsync** — front-end required-reason check, POST /admin/signup/{id}/reject, optimistic UI update to Rejected
- **RetryProvisionAsync** — POST /admin/tenants/{id}/provision with per-row loading state guard (`_isProvisioning && _provisioningId == id`)
- **CancelApproveModal / CancelRejectModal** — reset all related state

### Integration Tests
- `ApproveTenant_WhenPending_ChangesStatusToApproved` — seeds Pending signup, calls Handle directly, asserts Status == "Approved"
- `ApproveTenant_WithNote_PersistsNote` — asserts ReviewNote == "looks good" after approval with note
- `RejectSignup_WhenPending_ChangesStatusToRejected` — asserts Status == "Rejected" and ReviewNote == "incomplete info"

All 3 new tests pass (8 total TenantManagement tests pass).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Web project packages not restored after worktree merge**
- **Found during:** Task 2 verification
- **Issue:** `dotnet build IronMonkey.Web --no-restore` failed with `CS0234: System.IdentityModel.Tokens.Jwt` missing assembly reference. Package was in csproj but not restored in the worktree.
- **Fix:** Ran `dotnet restore IronMonkey.Web/IronMonkey.Web.csproj` before the final build.
- **Commit:** 4931563 (no separate commit needed — resolved by restore before build)

## Known Stubs

None — all approve/reject/provision handlers are fully implemented and wired.

## Verification Results

```
dotnet test IronMonkey.Tests --filter "FullyQualifiedName~TenantManagement"
Passed: 8, Failed: 0 — Duration: 23s

dotnet build IronMonkey.ApiService — Build succeeded, 0 errors
dotnet build IronMonkey.Web — Build succeeded, 0 errors
```

## Self-Check: PASSED
- IronMonkey.Tests/Integration/TenantManagement/ApproveAndProvisionTests.cs — FOUND
- IronMonkey.Tests/Integration/TenantManagement/RejectSignupRequestTests.cs — FOUND
- IronMonkey.Web/Components/Pages/Admin/Tenants/TenantManagement.razor contains HandleApproveAsync — VERIFIED
- Commits 1ab74f3 and 4931563 — FOUND
