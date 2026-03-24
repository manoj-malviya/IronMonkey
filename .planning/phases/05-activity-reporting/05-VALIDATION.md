---
phase: 5
slug: activity-reporting
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-24
---

# Phase 5 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 with Moq 4.20.72 |
| **Config file** | IronMonkey.Tests/IronMonkey.Tests.csproj |
| **Quick run command** | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~Phase5"` |
| **Full suite command** | `dotnet test IronMonkey.Tests` |
| **Estimated runtime** | ~45 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~Phase5"`
- **After every plan wave:** Run `dotnet test IronMonkey.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 45 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 05-01-01 | 01 | 0 | ALL | stub | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ActivityTimelineTests"` | ❌ W0 | ⬜ pending |
| 05-02-01 | 02 | 1 | ACTV-01 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ActivityLogInterceptorTests"` | ❌ W0 | ⬜ pending |
| 05-03-01 | 03 | 2 | ACTV-01 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ActivityTimelineTests"` | ❌ W0 | ⬜ pending |
| 05-04-01 | 04 | 2 | REPT-01 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~PipelineDashboardTests"` | ❌ W0 | ⬜ pending |
| 05-04-02 | 04 | 2 | REPT-02 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ConversionDashboardTests"` | ❌ W0 | ⬜ pending |
| 05-04-03 | 04 | 2 | REPT-03 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~AgentPerformanceTests"` | ❌ W0 | ⬜ pending |
| 05-05-01 | 05 | 3 | ALL | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~Phase5"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `IronMonkey.Tests/Integration/ActivityTimelineTests.cs` — stubs for ACTV-01
- [ ] `IronMonkey.Tests/Integration/PipelineDashboardTests.cs` — stubs for REPT-01
- [ ] `IronMonkey.Tests/Integration/ConversionDashboardTests.cs` — stubs for REPT-02
- [ ] `IronMonkey.Tests/Integration/AgentPerformanceTests.cs` — stubs for REPT-03
- [ ] `IronMonkey.Tests/Integration/ActivityLogInterceptorTests.cs` — stubs for activity tracking interceptor

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Dashboard updates without page reload | REPT-01 | Requires browser interaction | Navigate to pipeline dashboard, change lead stage in another tab, verify counts update |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 45s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
