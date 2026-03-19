---
phase: 1
slug: multi-tenancy-foundation
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-19
---

# Phase 1 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit (new test project — Wave 0 installs) |
| **Config file** | none — Wave 0 creates IronMonkey.Tests.csproj |
| **Quick run command** | `dotnet test --filter "Category=Unit"` |
| **Full suite command** | `dotnet test` |
| **Estimated runtime** | ~30 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test --filter "Category=Unit"`
- **After every plan wave:** Run `dotnet test`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 30 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 01-01-01 | 01 | 0 | TNCY-01 | integration | `dotnet test --filter "NameStarts=ProvisionTenantTests"` | ❌ W0 | ⬜ pending |
| 01-01-02 | 01 | 0 | TNCY-01 | integration | `dotnet test --filter "NameStarts=TenantProvisioningTests"` | ❌ W0 | ⬜ pending |
| 01-02-01 | 02 | 1 | TNCY-02 | integration | `dotnet test --filter "NameStarts=TenantIsolationTests"` | ❌ W0 | ⬜ pending |
| 01-02-02 | 02 | 1 | TNCY-02 | integration | `dotnet test --filter "NameStarts=CrossTenantSecurityTests"` | ❌ W0 | ⬜ pending |
| 01-03-01 | 03 | 1 | TNCY-02 | unit/integration | `dotnet test --filter "NameStarts=HangfireJobTenantTests"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `IronMonkey.Tests/IronMonkey.Tests.csproj` — xUnit test project with xUnit, Moq, TestContainers.PostgreSql dependencies
- [ ] `IronMonkey.Tests/Fixtures/PostgreSqlFixture.cs` — TestContainers PostgreSQL setup for integration tests
- [ ] `IronMonkey.Tests/Integration/TenantProvisioningTests.cs` — TNCY-01 stubs
- [ ] `IronMonkey.Tests/Integration/TenantIsolationTests.cs` — TNCY-02 stubs
- [ ] `IronMonkey.Tests/Integration/CrossTenantSecurityTests.cs` — TNCY-02 API security stubs
- [ ] `IronMonkey.Tests/Unit/HangfireJobTenantTests.cs` — Job tenant context stubs
- [ ] `appsettings.test.json` — Test configuration

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Platform admin dashboard renders approval UI | TNCY-01 | UI rendering validation | Login as platform admin, verify pending tenants list, approve/reject buttons visible |
| Provision button triggers DB creation | TNCY-01 | End-to-end provisioning flow | Approve tenant, click provision, verify new DB created in PostgreSQL |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
