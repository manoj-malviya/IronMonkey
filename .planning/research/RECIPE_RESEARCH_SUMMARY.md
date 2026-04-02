# Industry Recipe Research Summary

**Project:** IronMonkey v1.1 (Tenant Onboarding with Industry Recipes)
**Researched:** 2026-03-24
**Overall Confidence:** HIGH
**Downstream Consumer:** Roadmap creation (Phase structure, ordering, risks)

## What This Research Answers

**Question:** How do industry recipe templates integrate with the existing multi-tenant CRM architecture?

**Answer:** Recipes are reusable data templates stored in the central database, selected during signup, and applied to newly provisioned tenant databases. They don't require new technologies, modify existing schemas, or change core architecture — they extend the current provisioning flow with a data-seeding step.

## Key Research Findings

### 1. Integration Strategy: Data-Driven, Not Code-Driven

**Finding:** Recipes should be **data templates**, not code. Each industry recipe is a reusable configuration (stages, fields, workflows) stored in the central database and applied identically to every tenant choosing that recipe.

**Why this matters for roadmap:**
- ✓ No new dependencies or language features needed
- ✓ Same entity types (PipelineStage, CustomFieldDefinition, WorkflowRule) used everywhere
- ✓ Recipes survive code changes; can be updated without shipping new versions
- ✓ Scales to hundreds of recipes without architectural overhead

**Confidence:** HIGH (SaaS best practice + verified against v1.0 patterns)

---

### 2. Storage Model: CentralDb (Not TenantDb)

**Finding:** Recipes live in `CentralDbContext` (shared), not `TenantDbContext` (per-tenant). Each tenant gets **independent copies** of recipe data via provisioning.

```
CentralDb (One copy, shared):
├─ IndustryRecipe (Automobile Dealership) — reusable template
│  ├─ RecipeStageDefinition (Lead, Qualified, Sold, Lost)
│  ├─ RecipeFieldDefinition (VIN, Model Year, Interior Color)
│  └─ RecipeWorkflowDefinition (Auto-assign leads)
│
TenantDb (Per-tenant copy):
├─ PipelineStage (Lead, Qualified, Sold, Lost) — tenant's own copy
├─ CustomFieldDefinition (VIN, Model Year, Interior Color)
└─ WorkflowRule (Auto-assign leads)
```

**Why this matters for roadmap:**
- ✓ Single source of truth for recipe definitions
- ✓ No data sync complexity between central and tenant DBs
- ✓ Tenants can customize copied data freely; recipe unaffected
- ✓ Simple to version recipes: each new recipe = new IndustryRecipe row

**Confidence:** HIGH (verified against existing v1.0 multi-tenancy pattern)

---

### 3. Integration Points: Five Architectural Touchpoints

**Finding:** Recipes integrate at exactly 5 points in the existing system, all relatively isolated:

| # | Point | Change Type | Scope |
|---|-------|-------------|-------|
| 1 | App Startup | New | Recipe lookup service; optional in-memory cache |
| 2 | Signup Form | Modified | Add `recipeId` parameter; validate recipe exists |
| 3 | Recipe Listing | New | GET /recipes endpoint (public, no auth) |
| 4 | Approval Workflow | Modified | Allow admin to override selected recipe |
| 5 | Provisioning Service | Modified | Add ApplyRecipeAsync() method; call after migrations |

**Why this matters for roadmap:**
- ✓ Each point is independent and can be worked on separately
- ✓ No cascading dependencies between integration points
- ✓ Risks are localized (recipe lookup doesn't affect lead schema, etc.)
- ✓ Minimal impact on existing code (mostly additive)

**Confidence:** HIGH (analyzed against existing codebase)

---

### 4. Component Breakdown: 8 New, 5 Modified, 0 Deleted

**Finding:** Recipe implementation requires:
- **8 new components:** 5 entities + 1 EF configuration + 2 services/endpoints
- **5 modified components:** SignupRequest + 2 endpoints + 2 contexts
- **0 deleted components:** Fully backward compatible

**Why this matters for roadmap:**
- ✓ Minimal surface area to test (only 8 new things to implement)
- ✓ No breaking changes to existing code (safe to release)
- ✓ Existing integration tests still pass (no schema changes to TenantDb)
- ✓ Can be feature-flagged if needed (recipes optional)

**Confidence:** HIGH (component audit completed)

---

### 5. Build Order: Strict Sequencing Required

**Finding:** Work must follow specific phases; out-of-order work causes blocker dependencies.

```
Phase 1 (2-3 days): Recipe Data Model
├─ Build IndustryRecipe entities
├─ Create EF migration + seed data
└─ Update CentralDbContext
   ↓ (must complete)
Phase 2 (1-2 days): Recipe API
├─ Build ListRecipesEndpoint
└─ Register endpoints
   ↓ (can parallel with 1)
Phase 3 (1-2 days): Signup Integration
├─ Update SignupRequest entity
├─ Add recipe validation
└─ Create migration
   ↓ (depends on 1 + 2)
Phase 4 (2-3 days): Provisioning Integration
├─ Implement RecipeApplicationService
├─ Integrate into TenantProvisioningService
└─ Heavy integration testing
   ↓ (depends on 1 + 3)
Phase 5 (Future, v1.2+): Admin Recipe Management
└─ Admin endpoints for recipe CRUD
```

**Critical constraint:** Phase 4 cannot start until Phase 3 schema migration runs. SignupRequest must have SelectedRecipeId column before provisioning service reads it.

**Why this matters for roadmap:**
- ✓ Parallel work possible: Dev A on Phase 1, Dev B on Phase 2
- ✓ Phase 4 is longest (integration testing); start early in iteration
- ✓ Phase 5 can be deferred without breaking core v1.1
- ✓ Total effort: ~7-10 days for v1.1 core features

**Confidence:** HIGH (dependency analysis complete)

---

### 6. Risk Profile: Low-to-Medium Overall

**Finding:** Most risks are well-mitigated by existing v1.0 patterns.

| Risk | Severity | Mitigation | Confidence |
|------|----------|-----------|-----------|
| **Recipe lookup slow at signup** | LOW | Recipes are small objects; in-memory cache mitigates | HIGH |
| **Provisioning fails mid-recipe** | MEDIUM | Use transaction; rollback cleans up partial data | HIGH |
| **Admin selects invalid recipe** | LOW | API validation prevents invalid selection | HIGH |
| **Tenant modifies seeded recipe data** | NOT A RISK | Expected behavior; recipes are templates | HIGH |
| **Recipe schema evolves** | LOW | Immutable versions; new recipes = new rows | MEDIUM |
| **Seeding takes too long** | MEDIUM | ~100ms for typical recipe; monitor in tests | MEDIUM |

**Why this matters for roadmap:**
- ✓ No blocking risks discovered
- ✓ Familiar patterns mitigate most issues (v1.0 validation, transactions)
- ✓ Monitoring points identified for Phase 4 testing
- ✓ Can ship v1.1 with confidence

**Confidence:** MEDIUM-HIGH (based on SaaS patterns + v1.0 architecture)

---

## Roadmap Implications

### Recommended Phase Structure (v1.1)

```
PHASE 1: Recipe Data Model
  Goal: Establish recipe entity structure and initial seed data
  Effort: 2-3 days
  Outputs:
    - 5 new recipe entities in CentralDb
    - EF migration with seed data (Automobile, Education, Blank)
    - Updated CentralDbContext
  Quality Gates:
    - Migration runs without errors
    - Recipe entities can be queried from CentralDb
    - Unit tests for entity factories pass

PHASE 2: Recipe Discovery API
  Goal: Expose recipes for signup UI selection
  Effort: 1-2 days
  Outputs:
    - GET /recipes endpoint (public)
    - GET /recipes/{id} endpoint (optional)
    - Recipe metadata in response (id, name, description, counts)
  Quality Gates:
    - GET /recipes returns all active recipes
    - Response can be consumed by signup UI
    - Caching strategy verified (optional)

PHASE 3: Signup Integration
  Goal: Allow users to select recipe during signup
  Effort: 1-2 days
  Outputs:
    - SignupRequest now stores SelectedRecipeId
    - POST /auth/signup validates recipe selection
    - Admin approval workflow can override recipe
    - EF migration for schema change
  Quality Gates:
    - SignupRequest saves recipe selection
    - Validation rejects invalid recipes
    - Admin approval works with/without recipe override

PHASE 4: Provisioning Integration (HEAVY)
  Goal: Apply recipe templates to newly provisioned tenant DB
  Effort: 2-3 days (mostly testing)
  Outputs:
    - RecipeApplicationService deserializes & applies recipes
    - TenantProvisioningService calls recipe application
    - Full provisioning flow tested end-to-end
    - Integration tests verify stages/fields/workflows seeded
  Quality Gates:
    - Provisioning completes with recipe applied
    - Seeding performance acceptable (<5s per tenant)
    - Tenant can modify seeded data freely
    - Transaction rollback tested (failure case)

PHASE 5: Polish & Documentation (Optional)
  Goal: UI polish, feature flags, documentation
  Effort: 1 day
  Outputs:
    - Signup UI shows recipe selector
    - Admin approval UI displays selected recipe
    - Recipe descriptions shown clearly
    - Dev docs for future recipe management
```

### Ordering Rationale

**Why Phase 1 → 2 → 3 → 4?**

1. **Phase 1 first:** Foundation. Can't list recipes (Phase 2) without entities. Can't store selections (Phase 3) without data model.

2. **Phase 2 in parallel with 1:** Endpoints can be written while Phase 1 is in progress; just need to wait for migration.

3. **Phase 3 after 2:** Users need to see recipe list before selecting. Recipe validation in Phase 3 uses Phase 2 endpoint knowledge.

4. **Phase 4 last:** Most complex. Depends on Phase 1 (entities) + Phase 3 (schema). Do this when team is fresh; expect integration test surprises.

5. **Phase 5 optional:** Can be deferred to v1.1.1 or v1.2 if time tight. Core feature complete after Phase 4.

---

### Research Flags: Phase-Specific Deeper Investigation

| Phase | Topic | Flag | Why |
|-------|-------|------|-----|
| 1 | Recipe versioning | **INVESTIGATE IF:** Tenants expect recipe updates post-provisioning | Impacts future migration strategy |
| 2 | Recipe caching | **INVESTIGATE IF:** Signup page hits recipes endpoint on every load | Performance may require caching |
| 3 | Blank recipe handling | **INVESTIGATE IF:** "Blank" recipe should be special-cased or regular entity | Design choice; affects admin UI |
| 4 | Seeding performance | **INVESTIGATE IF:** Large recipes (100+ fields) slow provisioning unacceptably | May need async job queue |
| 4 | Transaction rollback | **INVESTIGATE IF:** Partial provisioning leaves orphaned data | Test failure scenarios |

**None of these block v1.1; all can be handled during Phase 4 testing.**

---

## Confidence Assessment

| Area | Level | Evidence | Gaps |
|------|-------|----------|------|
| **Integration architecture** | HIGH | v1.0 architecture reviewed; recipe patterns match SaaS best practices | None identified |
| **Entity design** | HIGH | 5 entities map cleanly to existing types; no schema conflicts | Recipe role definitions (v1.2 feature) TBD |
| **Data flow** | HIGH | Signup → approval → provisioning flow clear; recipe application isolated | Error handling details for Phase 4 |
| **Build order** | HIGH | Dependency chain analyzed; no circular dependencies | Task-level effort estimates may vary |
| **Performance** | MEDIUM | Typical recipe: 5-10 stages, 8-15 fields; seeding ~100ms estimated | Large recipes (100+ fields) untested |
| **Risk mitigation** | HIGH | Familiar patterns; transactions, validation proven in v1.0 | Long-term recipe versioning strategy pending |

---

## Comparative Analysis: Why This Approach

### Why NOT: Code-Driven Recipes
**Alternative:** Recipes defined in C# code (interfaces, classes)
- ✗ Require code deployment for new recipes
- ✗ Hard to customize per tenant
- ✗ Tightly coupled to application version
- ✗ Not admin-manageable

### Why NOT: Single JSON Blob
**Alternative:** Store entire recipe as one JSONB column
- ✗ Can't query individual stages/fields
- ✗ Less type-safe
- ✗ Harder to extend (add new recipe features)

### Why THIS: Normalized Entities in CentralDb
**Chosen approach:** Separate entities (IndustryRecipe, RecipeStageDefinition, etc.)
- ✓ Queryable (can filter recipes by stage count, etc.)
- ✓ Type-safe (C# entities)
- ✓ Extensible (add new recipe features in future)
- ✓ Admin-friendly (can be edited via UI in v1.2+)
- ✓ Follows IronMonkey v1.0 patterns exactly

---

## Existing Patterns Leveraged

IronMonkey v1.0 already has **all the building blocks** needed for recipes:

| v1.0 Pattern | Recipe Reuse |
|--------------|--------------|
| **Database-per-tenant isolation** | Recipes apply to isolated DBs; no cross-tenant conflicts |
| **Provisioning service** | Recipe application integrated into existing flow |
| **EF Core entity factories** | PipelineStage.Create(), CustomFieldDefinition.Create() used as-is |
| **CentralDbContext for shared data** | Recipes stored alongside Tenants, SignupRequests, ApiKeys |
| **Minimal API endpoints + FluentValidation** | Recipe endpoints follow identical pattern |
| **Domain events + outbox** | Recipe application could trigger events (future) |
| **JSONB for complex fields** | Recipe conditions/actions stored as JSON, same as workflows |

**Nothing new needs to be invented. Everything builds on v1.0 foundations.**

---

## Recommendations for Roadmap Creator

### 1. **Allocate 7-10 days total** for v1.1 core recipe features
   - Phase 1-4: 6-8 days (implementation + testing)
   - Phase 5: 1-2 days (polish, optional)

### 2. **Start Phase 1 immediately** — zero blockers
   - Entities and seed data can be built in parallel with other v1.1 work
   - Migration runs on Day 1-2; unblocks other phases

### 3. **Plan Phase 4 heavy testing** (2-3 days)
   - Most integration complexity here
   - Test scenarios: happy path, recipe override, missing recipe, partial failure, rollback
   - Performance profiling for large recipes

### 4. **Consider two recipes for v1.1 MVP**
   - Automobile Dealership (primary)
   - Educational Institution (secondary)
   - Blank recipe (always available)
   - More recipes can be added in v1.2+ without code changes

### 5. **Defer Phase 5** (Admin Recipe Management) to v1.2
   - v1.1 focus: user onboarding experience
   - v1.2 focus: admin recipe customization UI
   - Both can ship independently

### 6. **Plan integration tests heavily** in Phase 4
   - Happy path: full provisioning with recipe
   - Edge cases: no recipe, invalid recipe, recipe override
   - Failure modes: midway provisioning failure
   - Performance: seeding time <5s

### 7. **Feature flag for v1.1 launch** (optional but recommended)
   - Roll out to early beta tenants first
   - Measure: how many users select recipes vs blank?
   - Refine recipe recommendations if needed

---

## Out-of-Scope (For Future Milestones)

| Feature | Why Deferred | When to Revisit |
|---------|--------------|-----------------|
| **Recipe versioning** | Adds complexity; immutable snapshots sufficient for v1.1 | v1.2 (if tenants need updates) |
| **Recipe marketplace** | Requires rating system, approval workflow | v1.3+ (market maturity check) |
| **Recipe A/B testing** | Data collection/analytics needed first | v1.2+ (after usage data available) |
| **Recipe recommendations** | ML/analytics layer doesn't exist yet | v1.3+ (post-1M+ user scale) |
| **Custom recipe builder** | UI complexity; focus on preset recipes first | v1.2+ (demand validation) |

---

## Summary for Roadmap

**v1.1 Tenant Onboarding with Industry Recipes** is **low-risk, high-value** with:
- ✓ Clear phased structure (4-5 phases, no blockers between phases)
- ✓ Proven patterns (leverages 100% of v1.0 architecture)
- ✓ Minimal new code (8 entities/services; 5 modified components)
- ✓ Manageable effort (7-10 days for core feature)
- ✓ Deferrable components (Phase 5 optional for v1.1)

**Recommend:** Start Phase 1 immediately. Plan 2-person team (1 full-time, 1 split with other work). Allocate full week for Phase 4 integration testing. Ship core feature by end of sprint; admin management in v1.2.

