---
phase: 12
slug: user-role-management-ui
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-01
---

# Phase 12 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 with Testcontainers (postgres:15-alpine) |
| **Config file** | IronMonkey.Tests/IronMonkey.Tests.csproj |
| **Quick run command** | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~UserManagement"` |
| **Full suite command** | `dotnet test IronMonkey.Tests` |
| **Estimated runtime** | ~30 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~UserManagement"`
- **After every plan wave:** Run `dotnet test IronMonkey.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 30 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 12-01-01 | 01 | 1 | USUI-01 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ListUsersEndpoint"` | ❌ W0 | ⬜ pending |
| 12-01-02 | 01 | 1 | USUI-02 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~CreateUserEndpoint"` | ❌ W0 | ⬜ pending |
| 12-01-03 | 01 | 1 | USUI-03 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~UpdateUserEndpoint"` | ❌ W0 | ⬜ pending |
| 12-01-04 | 01 | 1 | USUI-04 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~DeactivateUserEndpoint"` | ❌ W0 | ⬜ pending |
| 12-02-01 | 02 | 2 | USUI-01 | manual | Browser: navigate /admin/users, verify table renders | N/A | ⬜ pending |
| 12-02-02 | 02 | 2 | USUI-02 | manual | Browser: create user, verify password display | N/A | ⬜ pending |
| 12-03-01 | 03 | 2 | USUI-03 | manual | Browser: edit user, verify save | N/A | ⬜ pending |
| 12-03-02 | 03 | 2 | USUI-04 | manual | Browser: deactivate user, verify status change | N/A | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `IronMonkey.Tests/Integration/UserManagement/ListUsersEndpointTests.cs` — stubs for USUI-01
- [ ] `IronMonkey.Tests/Integration/UserManagement/CreateUserEndpointTests.cs` — stubs for USUI-02
- [ ] `IronMonkey.Tests/Integration/UserManagement/UpdateUserEndpointTests.cs` — stubs for USUI-03
- [ ] `IronMonkey.Tests/Integration/UserManagement/DeactivateUserEndpointTests.cs` — stubs for USUI-04

*Existing PostgreSqlFixture infrastructure covers test container setup.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| User list table renders with correct columns | USUI-01 | Visual UI rendering | Navigate to /admin/users, verify Name/Email/Role/Status/Date columns |
| Create user shows auto-generated password | USUI-02 | UI interaction + clipboard | Fill form, submit, verify password displayed in success banner |
| Edit user pre-populates form | USUI-03 | UI interaction | Click Edit, verify fields pre-filled, modify and save |
| Reset password displays new password | USUI-03 | UI interaction | Click Reset Password on edit page, verify new password shown |
| Deactivate modal and status update | USUI-04 | UI interaction | Click Deactivate, confirm in modal, verify badge changes |
| Show Deactivated toggle | USUI-01 | UI interaction | Toggle checkbox, verify deactivated users appear/disappear |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
