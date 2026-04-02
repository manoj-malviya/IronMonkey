---
phase: 11
slug: tenant-management-ui
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-01
---

# Phase 11 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 with Testcontainers (postgres:15-alpine) |
| **Config file** | IronMonkey.Tests/IronMonkey.Tests.csproj |
| **Quick run command** | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~TenantManagement"` |
| **Full suite command** | `dotnet test IronMonkey.Tests` |
| **Estimated runtime** | ~30 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~TenantManagement"`
- **After every plan wave:** Run `dotnet test IronMonkey.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 30 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 11-01-01 | 01 | 1 | TNUI-01 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ListTenantsEndpoint"` | ❌ W0 | ⬜ pending |
| 11-01-02 | 01 | 1 | TNUI-02 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~GetSignupRequestDetail"` | ❌ W0 | ⬜ pending |
| 11-02-01 | 02 | 1 | TNUI-01 | manual | Browser: navigate /admin/tenants, verify table renders | N/A | ⬜ pending |
| 11-02-02 | 02 | 1 | TNUI-02 | manual | Browser: expand signup row, verify details display | N/A | ⬜ pending |
| 11-03-01 | 03 | 2 | TNUI-03 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ApproveAndProvision"` | ❌ W0 | ⬜ pending |
| 11-03-02 | 03 | 2 | TNUI-04 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RejectSignupRequest"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `IronMonkey.Tests/Integration/TenantManagement/ListTenantsEndpointTests.cs` — stubs for TNUI-01
- [ ] `IronMonkey.Tests/Integration/TenantManagement/GetSignupRequestDetailTests.cs` — stubs for TNUI-02
- [ ] `IronMonkey.Tests/Integration/TenantManagement/ApproveAndProvisionTests.cs` — stubs for TNUI-03
- [ ] `IronMonkey.Tests/Integration/TenantManagement/RejectSignupRequestTests.cs` — stubs for TNUI-04

*Existing PostgreSqlFixture infrastructure covers test container setup.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Tenant list table renders with correct columns | TNUI-01 | Visual UI rendering | Navigate to /admin/tenants, verify Name/Status/Plan/Provisioned/Recipe/Date columns |
| Expandable row shows signup details | TNUI-02 | UI interaction | Click signup row, verify all detail fields display |
| Approve modal triggers provisioning | TNUI-03 | Multi-step UI flow | Click Approve, enter note, confirm, verify spinner then success |
| Reject modal with reason | TNUI-04 | UI interaction | Click Reject, enter reason, confirm, verify status badge changes |
| Tab switching between Tenants and Signup Requests | N/A | UI interaction | Click each tab, verify correct content loads |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
