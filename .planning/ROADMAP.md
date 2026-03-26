# Roadmap: IronMonkey — Lead Management System

## Milestones

- v1.0 MVP — Phases 1-5 (shipped 2026-03-24) | [Archive](milestones/v1.0-ROADMAP.md)
- v1.1 Tenant Onboarding with Industry Recipes — Phases 6-8 (in progress)

## Phases

<details>
<summary>v1.0 MVP (Phases 1-5) — SHIPPED 2026-03-24</summary>

- [x] Phase 1: Multi-Tenancy Foundation (6/6 plans) — completed 2026-03-20
- [x] Phase 2: Configurable Lead Model (6/6 plans) — completed 2026-03-20
- [x] Phase 3: Lead Ingestion (7/7 plans) — completed 2026-03-21
- [x] Phase 4: Pipeline & Workflow Engine (6/6 plans) — completed 2026-03-22
- [x] Phase 5: Activity & Reporting (5/5 plans) — completed 2026-03-24

</details>

### v1.1 Tenant Onboarding with Industry Recipes (In Progress)

**Milestone Goal:** New tenants pick an industry at signup and get a fully pre-configured workspace — pipeline stages, custom fields, workflow rules, and default roles — as a starting point they can freely customize.

- [ ] **Phase 6: Recipe Data Model** - Establish the central database entities and seeding infrastructure that all recipes build on
- [ ] **Phase 7: Recipe Content** - Define and seed the Automobile Dealership and Educational Institution recipes plus the Blank/Custom option
- [ ] **Phase 8: Onboarding & Admin API** - Wire recipe selection into tenant signup, expose recipe preview and management endpoints

## Phase Details

### Phase 6: Recipe Data Model
**Goal**: The system can store, version, and apply industry recipe templates atomically during tenant provisioning
**Depends on**: Phase 5 (v1.0 complete)
**Requirements**: RCPE-01, RCPE-02, RCPE-03, RCPE-04
**Success Criteria** (what must be TRUE):
  1. An IndustryRecipe entity exists in the central database storing pipeline stages, custom fields, workflow rules, and default roles as JSONB
  2. Each recipe carries metadata (name, description, icon/identifier) readable by the selection UI
  3. A Blank/Custom recipe exists and seeds a minimal working tenant (one default stage, Admin role) on provisioning
  4. Recipes carry a version field that increments when a recipe template is updated
  5. Applying a recipe to a tenant database is transactional — any failure rolls back completely with no partial state left behind
**Plans**: 3 plans

Plans:
- [ ] 06-01-PLAN.md — IndustryRecipe entity, RecipeContentModel DTOs, EF configuration, CentralDbContext registration, Tenant recipe tracking fields
- [ ] 06-02-PLAN.md — EF Core migrations: industry_recipes table, Tenant recipe columns, Blank/Custom recipe data seed
- [ ] 06-03-PLAN.md — TenantProvisioningService recipe application extension and integration tests

### Phase 7: Recipe Content
**Goal**: Automobile Dealership and Educational Institution recipes are fully defined and produce correct, queryable tenant data when applied
**Depends on**: Phase 6
**Requirements**: RCNT-01, RCNT-02, RCNT-03
**Success Criteria** (what must be TRUE):
  1. Provisioning with the Automobile recipe creates the road-to-the-sale pipeline stages, vehicle-specific custom fields, follow-up workflow rules, and sales team roles in the tenant database
  2. Provisioning with the Educational Institution recipe creates the admissions funnel stages, student-specific custom fields, notification workflow rules, and admissions team roles in the tenant database
  3. After provisioning with either domain recipe, the tenant's pipeline board shows sample leads so the workspace is immediately non-empty and explorable
  4. All recipe-seeded stages, fields, rules, and roles are modifiable by the tenant after provisioning — no entity is locked or read-only
**Plans**: TBD
**UI hint**: yes

### Phase 8: Onboarding & Admin API
**Goal**: Users can select a recipe at signup with a preview of what it includes, and administrators can manage the recipe catalog via API
**Depends on**: Phase 7
**Requirements**: ONBD-01, ONBD-02, ONBD-03, ONBD-04, ONBD-05, RADM-01, RADM-02, RADM-03
**Success Criteria** (what must be TRUE):
  1. The signup flow exposes an industry selection step; submitting with a recipe ID provisions the tenant with that recipe applied atomically
  2. A caller can request the list of available recipes (GET /api/recipes) and receive names, descriptions, and stage/field/rule counts
  3. A caller can preview the full contents of a specific recipe (stages, fields, rules, roles) before committing to it during signup
  4. An admin can create a new recipe, update an existing recipe definition, and soft-delete a recipe so it no longer appears in the selection list
**Plans**: TBD
**UI hint**: yes

## Progress

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1. Multi-Tenancy Foundation | v1.0 | 6/6 | Complete | 2026-03-20 |
| 2. Configurable Lead Model | v1.0 | 6/6 | Complete | 2026-03-20 |
| 3. Lead Ingestion | v1.0 | 7/7 | Complete | 2026-03-21 |
| 4. Pipeline & Workflow Engine | v1.0 | 6/6 | Complete | 2026-03-22 |
| 5. Activity & Reporting | v1.0 | 5/5 | Complete | 2026-03-24 |
| 6. Recipe Data Model | v1.1 | 0/3 | Not started | - |
| 7. Recipe Content | v1.1 | 0/? | Not started | - |
| 8. Onboarding & Admin API | v1.1 | 0/? | Not started | - |
