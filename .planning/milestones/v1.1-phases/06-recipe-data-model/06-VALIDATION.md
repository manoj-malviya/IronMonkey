---
phase: 6
slug: recipe-data-model
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-26
---

# Phase 6 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 with Testcontainers.PostgreSql 4.3.0 |
| **Config file** | None (xUnit uses convention-based discovery) |
| **Quick run command** | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~TenantProvisioningTests" -x` |
| **Full suite command** | `dotnet test IronMonkey.Tests -x` |
| **Estimated runtime** | ~30 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~TenantProvisioningTests" -x`
- **After every plan wave:** Run `dotnet test IronMonkey.Tests -x`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 30 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 06-01-01 | 01 | 1 | RCPE-01 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~IndustryRecipeEntityTests"` | ❌ W0 | ⬜ pending |
| 06-01-02 | 01 | 1 | RCPE-02 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~IndustryRecipeEntityTests"` | ❌ W0 | ⬜ pending |
| 06-02-01 | 02 | 2 | RCPE-03 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~BlankRecipeProvisioningTests"` | ❌ W0 | ⬜ pending |
| 06-03-01 | 03 | 2 | RCPE-04 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~IndustryRecipeEntityTests"` | ❌ W0 | ⬜ pending |
| 06-04-01 | 04 | 3 | RCPE-01,RCPE-03 | integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeProvisioningTests"` | ❌ W0 | ⬜ pending |

*Note: RCPE-04 version testing is covered by `IndustryRecipeEntityTests` Test 4 (`IndustryRecipe_Version_StartsAtOne`), co-located with other entity tests in Plan 01 Task 1.*

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `IronMonkey.Tests/Integration/IndustryRecipeEntityTests.cs` — stubs for RCPE-01, RCPE-02, and RCPE-04 versioning (Test 4)
- [ ] `IronMonkey.Tests/Integration/BlankRecipeProvisioningTests.cs` — stubs for RCPE-03
- [ ] `IronMonkey.Tests/Integration/RecipeProvisioningTests.cs` — stubs for recipe-driven provisioning

*Existing infrastructure (PostgreSqlFixture, TenantProvisioningTests) covers test setup.*

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
