---
phase: 7
slug: recipe-content
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-26
---

# Phase 7 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 with Testcontainers.PostgreSql 4.3.0 |
| **Config file** | None (convention-based discovery) |
| **Quick run command** | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~Recipe" -x` |
| **Full suite command** | `dotnet test IronMonkey.Tests -x` |
| **Estimated runtime** | ~30 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~Recipe" -x`
- **After every plan wave:** Run `dotnet test IronMonkey.Tests -x`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 30 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 07-01-01 | 01 | 1 | RCNT-01 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~AutomobileRecipeProvisioningTests"` | ❌ W0 | ⬜ pending |
| 07-01-02 | 01 | 1 | RCNT-02 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~EducationRecipeProvisioningTests"` | ❌ W0 | ⬜ pending |
| 07-02-01 | 02 | 2 | RCNT-03 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~SampleLeads"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `IronMonkey.Tests/Integration/AutomobileRecipeProvisioningTests.cs` — stubs for RCNT-01 (stages, fields, rules, roles)
- [ ] `IronMonkey.Tests/Integration/EducationRecipeProvisioningTests.cs` — stubs for RCNT-02 (stages, fields, rules, roles)
- [ ] Sample lead provisioning tests within both test classes — stubs for RCNT-03

*Existing infrastructure (PostgreSqlFixture, TenantProvisioningService) covers test setup.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| All recipe-seeded config is modifiable | ONBD-04 | Requires API calls to update seeded entities | Deferred to Phase 8 testing |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
