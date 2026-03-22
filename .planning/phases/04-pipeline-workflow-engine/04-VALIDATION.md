---
phase: 04
slug: pipeline-workflow-engine
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-22
---

# Phase 04 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 with Testcontainers PostgreSQL 4.3.0 |
| **Config file** | None — tests use PostgreSqlFixture (shared) and unique DB per test (GUID suffix) |
| **Quick run command** | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~Pipeline\|Task\|Routing\|WorkflowRule\|StateTransition"` |
| **Full suite command** | `dotnet test IronMonkey.Tests` |
| **Estimated runtime** | ~60 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~Pipeline\|Task\|Routing\|WorkflowRule\|StateTransition"`
- **After every plan wave:** Run `dotnet test IronMonkey.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 60 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 04-01-01 | 01 | 0 | PIPE-01 | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~KanbanBoardTests"` | ❌ W0 | ⬜ pending |
| 04-01-02 | 01 | 0 | PIPE-02 | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~TaskTests"` | ❌ W0 | ⬜ pending |
| 04-01-03 | 01 | 0 | PIPE-03 | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~LeadRoutingTests"` | ❌ W0 | ⬜ pending |
| 04-01-04 | 01 | 0 | PIPE-04 | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~WorkflowRuleTests"` | ❌ W0 | ⬜ pending |
| 04-01-05 | 01 | 0 | PIPE-05 | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~StateTransitionTests"` | ❌ W0 | ⬜ pending |
| 04-01-06 | 01 | 0 | PIPE-05 | Unit | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~StageTransitionConfigurationTests"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `IronMonkey.Tests/Integration/KanbanBoardTests.cs` — stubs for PIPE-01 (move lead, invalid transition error)
- [ ] `IronMonkey.Tests/Integration/TaskTests.cs` — stubs for PIPE-02 (create task, list by assignee)
- [ ] `IronMonkey.Tests/Integration/LeadRoutingTests.cs` — stubs for PIPE-03 (round-robin assignment, routing config)
- [ ] `IronMonkey.Tests/Integration/WorkflowRuleTests.cs` — stubs for PIPE-04 (rule evaluation, trigger firing)
- [ ] `IronMonkey.Tests/Integration/StateTransitionTests.cs` — stubs for PIPE-05 (transition validation, error messages)
- [ ] `IronMonkey.Tests/Unit/StageTransitionConfigurationTests.cs` — stubs for state machine graph setup

*Framework install: Stateless 5.20.1 and RulesEngine 5.1.2 needed in test project*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Kanban board renders leads grouped by stage | PIPE-01 | Visual rendering requires browser | Navigate to pipeline board, verify columns match stages, cards show correct info |
| Drag-drop with optimistic update and snap-back | PIPE-01 | Drag interaction cannot be tested via unit/integration | Drag a card to valid stage (should move), drag to invalid stage (should snap back with toast) |
| In-app notification bell shows badge | PIPE-04 | UI notification display requires browser | Trigger a workflow rule, verify notification badge appears |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 60s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
