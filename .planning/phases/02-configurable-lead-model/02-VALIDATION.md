---
phase: 2
slug: configurable-lead-model
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-20
---

# Phase 2 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 + TestContainers PostgreSQL |
| **Config file** | IronMonkey.Tests/IronMonkey.Tests.csproj |
| **Quick run command** | `dotnet test IronMonkey.Tests/IronMonkey.Tests.csproj --filter "FullyQualifiedName~Phase2"` |
| **Full suite command** | `dotnet test IronMonkey.Tests/IronMonkey.Tests.csproj` |
| **Estimated runtime** | ~60 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test IronMonkey.Tests/IronMonkey.Tests.csproj --filter "FullyQualifiedName~Phase2"`
- **After every plan wave:** Run `dotnet test IronMonkey.Tests/IronMonkey.Tests.csproj`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 60 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 02-01-01 | 01 | 1 | LEAD-01 | integration | `dotnet test --filter "CustomField"` | ❌ W0 | ⬜ pending |
| 02-02-01 | 02 | 1 | LEAD-02 | integration | `dotnet test --filter "PipelineStage"` | ❌ W0 | ⬜ pending |
| 02-03-01 | 03 | 2 | LEAD-03 | integration | `dotnet test --filter "LeadSource"` | ❌ W0 | ⬜ pending |
| 02-04-01 | 04 | 3 | LEAD-04 | integration | `dotnet test --filter "DuplicateDetection"` | ❌ W0 | ⬜ pending |
| 02-05-01 | 05 | 3 | LEAD-05 | integration | `dotnet test --filter "LeadMerge"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `IronMonkey.Tests/Integration/CustomFieldTests.cs` — stubs for LEAD-01
- [ ] `IronMonkey.Tests/Integration/PipelineStageTests.cs` — stubs for LEAD-02
- [ ] `IronMonkey.Tests/Integration/LeadSourceTests.cs` — stubs for LEAD-03
- [ ] `IronMonkey.Tests/Integration/DuplicateDetectionTests.cs` — stubs for LEAD-04
- [ ] `IronMonkey.Tests/Integration/LeadMergeTests.cs` — stubs for LEAD-05

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Custom fields appear on lead form | LEAD-01 | UI rendering | Create custom field via API, verify form endpoint returns field definition |
| Lead detail view shows source | LEAD-03 | UI rendering | Create lead with source, verify detail endpoint includes source value |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 60s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
