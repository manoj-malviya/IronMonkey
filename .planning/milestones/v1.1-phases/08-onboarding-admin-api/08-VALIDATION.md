---
phase: 8
slug: onboarding-admin-api
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-26
---

# Phase 8 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 with Testcontainers |
| **Config file** | Tests/Fixtures/PostgreSqlFixture.cs |
| **Quick run command** | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeEndpointTests" -x` |
| **Full suite command** | `dotnet test IronMonkey.Tests -x` |
| **Estimated runtime** | ~30 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeEndpointTests" -x`
- **After every plan wave:** Run `dotnet test IronMonkey.Tests -x`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 30 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 08-01-01 | 01 | 1 | ONBD-01 | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~SignupRecipeTests"` | ❌ W0 | ⬜ pending |
| 08-02-01 | 02 | 1 | ONBD-05 | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeEndpointTests.ListRecipes"` | ❌ W0 | ⬜ pending |
| 08-02-02 | 02 | 1 | ONBD-02 | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeEndpointTests.PreviewRecipe"` | ❌ W0 | ⬜ pending |
| 08-03-01 | 03 | 2 | RADM-01 | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeEndpointTests.CreateRecipe"` | ❌ W0 | ⬜ pending |
| 08-03-02 | 03 | 2 | RADM-02 | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeEndpointTests.UpdateRecipe"` | ❌ W0 | ⬜ pending |
| 08-03-03 | 03 | 2 | RADM-03 | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeEndpointTests.DeactivateRecipe"` | ❌ W0 | ⬜ pending |
| 08-01-02 | 01 | 1 | ONBD-03 | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeProvisioningTests"` | ✅ | ⬜ pending |
| 08-01-03 | 01 | 1 | ONBD-04 | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeProvisioningTests"` | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `IronMonkey.Tests/Integration/RecipeEndpointTests.cs` — stubs for ONBD-02, ONBD-05, RADM-01, RADM-02, RADM-03
- [ ] `IronMonkey.Tests/Integration/SignupRecipeTests.cs` — stubs for ONBD-01
- [ ] SuperAdmin JWT context fixture for admin endpoint tests

*Existing infrastructure covers ONBD-03 and ONBD-04 (RecipeProvisioningTests from Phase 6).*

---

## Manual-Only Verifications

*All phase behaviors have automated verification.*

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
