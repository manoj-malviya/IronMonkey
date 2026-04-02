---
phase: 13
slug: system-configuration-ui
status: draft
nyquist_compliant: true
wave_0_complete: true
created: 2026-04-01
---

# Phase 13 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 with Testcontainers (postgres:15-alpine) |
| **Config file** | IronMonkey.Tests/IronMonkey.Tests.csproj |
| **Quick run command** | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~SystemConfiguration"` |
| **Full suite command** | `dotnet test IronMonkey.Tests` |
| **Estimated runtime** | ~30 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~SystemConfiguration"`
- **After every plan wave:** Run `dotnet test IronMonkey.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 30 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 13-01-01 | 01 | 1 | CFUI-01 | integration | `dotnet test --filter "DeletePipelineStage"` | ❌ W0 | ⬜ pending |
| 13-01-02 | 01 | 1 | CFUI-02 | integration | `dotnet test --filter "UpdateCustomField"` | ❌ W0 | ⬜ pending |
| 13-01-03 | 01 | 1 | CFUI-02 | integration | `dotnet test --filter "DeleteCustomField"` | ❌ W0 | ⬜ pending |
| 13-01-04 | 01 | 1 | CFUI-04 | integration | `dotnet test --filter "DeleteWorkflowRule"` | ❌ W0 | ⬜ pending |
| 13-02-01 | 02 | 2 | CFUI-01..04 | manual | Browser: navigate /admin/configuration, verify 4 tabs | N/A | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] Test stubs for 4 new backend endpoints (delete stage, update/delete field, delete rule)

*Existing PostgreSqlFixture infrastructure covers test container setup.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| 4-tab page renders correctly | All | Visual UI | Navigate to /admin/configuration, verify all tabs load |
| Inline editing works | CFUI-01,02,04 | UI interaction | Click row, verify edit fields appear, save, verify update |
| Stage reorder via arrows | CFUI-01 | UI interaction | Click up/down arrows, verify order changes |
| Routing mode selector | CFUI-03 | UI interaction | Select mode, verify form changes, save |
| Workflow rule toggle | CFUI-04 | UI interaction | Toggle IsActive, verify API call |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
