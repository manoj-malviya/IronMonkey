---
phase: 10
slug: recipe-management-ui
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-01
---

# Phase 10 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 with Testcontainers (postgres:15-alpine) |
| **Config file** | IronMonkey.Tests/IronMonkey.Tests.csproj |
| **Quick run command** | `dotnet build IronMonkey.Web` |
| **Full suite command** | `dotnet test IronMonkey.Tests` |
| **Estimated runtime** | ~30 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet build IronMonkey.Web` (compile check)
- **After every plan wave:** Run `dotnet test IronMonkey.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 30 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 10-01-01 | 01 | 1 | RCUI-01 | build | `dotnet build IronMonkey.Web` | ✅ | ⬜ pending |
| 10-02-01 | 02 | 1 | RCUI-02, RCUI-03 | build | `dotnet build IronMonkey.Web` | ✅ | ⬜ pending |
| 10-03-01 | 03 | 2 | RCUI-04 | build | `dotnet build IronMonkey.Web` | ✅ | ⬜ pending |
| 10-03-02 | 03 | 2 | RCUI-05 | manual | Browser: deactivate recipe, verify status change | N/A | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] Existing test suite continues to pass (`dotnet test IronMonkey.Tests`)
- [ ] IronMonkey.Web builds with Phase 9 changes intact

*Existing test infrastructure covers build verification. Backend recipe API tests already exist.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Recipe list shows table with correct columns | RCUI-01 | Visual layout | Log in → navigate to /admin/recipes → verify table with Name, Industry, Status columns |
| Recipe form creates new recipe | RCUI-02 | Full E2E flow | Fill form at /admin/recipes/create → submit → verify recipe appears in list |
| Recipe edit saves changes | RCUI-03 | Full E2E flow | Open existing recipe → modify name → save → verify change persists |
| Recipe preview shows all content tabs | RCUI-04 | Visual layout | Open recipe preview → verify Stages, Fields, Rules, Roles, Sample Leads tabs |
| Recipe deactivation updates status | RCUI-05 | State change in UI | Click deactivate → confirm → verify badge changes to inactive |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
