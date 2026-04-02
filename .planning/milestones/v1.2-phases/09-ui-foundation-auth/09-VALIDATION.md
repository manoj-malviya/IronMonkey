---
phase: 9
slug: ui-foundation-auth
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-31
---

# Phase 9 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 with Testcontainers (postgres:15-alpine) |
| **Config file** | IronMonkey.Tests/IronMonkey.Tests.csproj |
| **Quick run command** | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~Phase9"` |
| **Full suite command** | `dotnet test IronMonkey.Tests` |
| **Estimated runtime** | ~30 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet build IronMonkey.sln` (compile check)
- **After every plan wave:** Run `dotnet test IronMonkey.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 30 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 09-01-01 | 01 | 1 | UIFN-05 | build | `dotnet build IronMonkey.Web` | ❌ W0 | ⬜ pending |
| 09-02-01 | 02 | 1 | UIFN-01 | integration | `dotnet test --filter "FullyQualifiedName~LoginPage"` | ❌ W0 | ⬜ pending |
| 09-02-02 | 02 | 1 | UIFN-02 | integration | `dotnet test --filter "FullyQualifiedName~AuthRedirect"` | ❌ W0 | ⬜ pending |
| 09-03-01 | 03 | 2 | UIFN-03 | manual | Browser: verify sidebar groups visible | N/A | ⬜ pending |
| 09-03-02 | 03 | 2 | UIFN-04 | manual | Browser: resize to <768px, verify collapse | N/A | ⬜ pending |
| 09-03-03 | 03 | 2 | UIFN-06 | integration | `dotnet test --filter "FullyQualifiedName~Logout"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] Tailwind CSS standalone CLI installed and MSBuild target configured
- [ ] `IronMonkey.Web` builds with Tailwind CSS compilation
- [ ] Existing test suite continues to pass (`dotnet test IronMonkey.Tests`)

*Existing test infrastructure (xUnit + Testcontainers) covers integration test needs.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Sidebar grouped navigation visible | UIFN-03 | Visual layout verification | Log in → verify sidebar shows 4 groups: Recipes, Tenants, Users & Roles, Configuration |
| Sidebar collapses on small screens | UIFN-04 | Responsive behavior requires browser | Resize browser to <768px → verify hamburger appears, sidebar overlays on tap |
| Login form styling | UIFN-01 | Visual appearance | Navigate to /login → verify centered card form on neutral background |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
