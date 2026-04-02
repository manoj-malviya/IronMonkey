# Phase 6: Recipe Data Model - Context

**Gathered:** 2026-03-26
**Status:** Ready for planning

<domain>
## Phase Boundary

Establish the central database entities and seeding infrastructure that all recipes build on. The system can store, version, and apply industry recipe templates atomically during tenant provisioning. Does not include specific recipe content (Phase 7) or onboarding UI/admin endpoints (Phase 8).

</domain>

<decisions>
## Implementation Decisions

### Recipe Entity Structure
- **D-01:** IndustryRecipe entity lives in CentralDbContext — recipes are platform-level templates, not per-tenant data
- **D-02:** Single JSONB column (`ContentJson`) stores the full recipe payload with typed sections: pipeline stages, custom field definitions, workflow rules, and default roles. Follows the existing JSONB custom field pattern (HasConversion)
- **D-03:** Flat metadata properties on the entity — Name (string, required), Description (string), IconIdentifier (string, for UI display), IndustrySlug (string, unique identifier like "automobile-dealership")
- **D-04:** IsBlank flag distinguishes the Blank/Custom recipe from domain recipes — enables uniform handling in provisioning (no special-case branching)
- **D-05:** IsActive flag for soft-delete/deactivation — deactivated recipes don't appear in selection list but remain in DB for audit trail

### Recipe Application Strategy
- **D-06:** Extend existing `TenantProvisioningService.SeedTenantDataAsync` — recipe application is richer seed data, not a separate service. The provisioning service accepts a recipe ID and applies it during tenant creation
- **D-07:** Add entities in dependency order within a single `SaveChangesAsync` call: roles first, then pipeline stages, then custom field definitions, then workflow rules. EF Core handles FK resolution within the transaction
- **D-08:** Full transactional atomicity — if any part of recipe application fails, the entire tenant provisioning rolls back. No partial state. Leverages the existing transactional pattern in `TenantProvisioningService`
- **D-09:** Recipe content is **copied** into tenant entities at provisioning time — no FK back to the recipe template. Tenant can freely modify all seeded data. This is already decided in PROJECT.md

### Versioning Mechanics
- **D-10:** Integer `Version` field on IndustryRecipe, starting at 1, incremented on each recipe update
- **D-11:** Store `AppliedRecipeId` (Guid?) and `AppliedRecipeVersion` (int?) on the Tenant entity — records which recipe and version were used at provisioning. Enables future upgrade tooling (deferred to v1.2)
- **D-12:** No recipe upgrade/migration for existing tenants in v1.1 — recipes are initial provisioning only (per REQUIREMENTS.md Out of Scope)

### Blank/Custom Recipe Design
- **D-13:** Blank recipe seeds: one pipeline stage named "New" with StageType=Entry + Admin role (already seeded by migration). Minimal viable workspace so tenant isn't staring at a blank screen
- **D-14:** Blank recipe is stored as a regular IndustryRecipe row with `IsBlank = true` — provisioning logic treats it identically to domain recipes, just with minimal content
- **D-15:** Blank recipe is seeded via EF Core migration (data seed) so it exists on first deployment — not created via admin API

### Claude's Discretion
- JSONB content structure design (section names, nesting depth, type discrimination)
- EF Core HasConversion implementation for recipe content serialization
- IndustryRecipe entity configuration (index strategy, max lengths)
- Recipe content C# model classes (strongly-typed DTOs for the JSONB sections)
- Exact changes to TenantProvisioningService method signatures
- Migration naming and structure
- Test fixture setup for recipe-based provisioning tests

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Recipe requirements
- `.planning/REQUIREMENTS.md` §Recipe Data Model — RCPE-01 through RCPE-04 definitions
- `.planning/ROADMAP.md` §Phase 6 — Success criteria (5 items) defining what must be TRUE

### Project decisions
- `.planning/PROJECT.md` §Key Decisions — "Recipes as JSONB in CentralDbContext", "Blank/Custom seeds defaults", "Version field from day one", "No upgrade tooling in v1.1"
- `.planning/STATE.md` §Blockers/Concerns — Transactional seeding dependency ordering, domain recipe validation risks

### Upstream phase artifacts
- `.planning/phases/01-multi-tenancy-foundation/01-CONTEXT.md` — Tenant provisioning flow, CentralDbContext/TenantDbContext split, seed data patterns
- `.planning/phases/04-pipeline-workflow-engine/04-CONTEXT.md` — PipelineStage StageType enum, WorkflowRule entity, RoutingConfig, state machine model

### Key existing code references
- `IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs` — Current provisioning flow (7 steps), `SeedTenantDataAsync` method to extend
- `IronMonkey.Data/CentralDbContext.cs` — Central DB entity registration, where IndustryRecipe DbSet will be added
- `IronMonkey.Data/Entities/Tenant.cs` — Tenant entity to extend with AppliedRecipeId/AppliedRecipeVersion
- `IronMonkey.Data/Entities/SignupRequest.cs` — SignupRequest.IndustryType field (existing) that can drive recipe selection
- `IronMonkey.Data/Entities/PipelineStage.cs` — StageType enum and entity structure that recipes will seed
- `IronMonkey.Data/Entities/CustomFieldDefinition.cs` — Custom field entity that recipes will seed
- `IronMonkey.Data/Entities/WorkflowRule.cs` — Workflow rule entity that recipes will seed
- `IronMonkey.Data/Entities/Role.cs` — Role entity (already seeded by migration) that recipes reference

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `TenantProvisioningService` — 7-step provisioning flow with transactional pattern; extend Step 5 (SeedTenantDataAsync) to apply recipe content
- `CentralDbContext` — registration pattern for new DbSet<IndustryRecipe>; follow existing Tenant/SignupRequest configuration style
- `SignupRequest.IndustryType` — already captures industry at signup; can map to recipe selection
- `Role` entity with migration-seeded defaults (SuperAdmin=1, Admin=201, Owner=301, TeleCaller=302) — recipe roles extend this pattern
- `CustomFieldDefinition` with JSONB HasConversion — template for recipe content JSONB serialization
- `TenantConfiguration` / `SignupRequestConfiguration` — template for IndustryRecipeConfiguration

### Established Patterns
- Entity factory: `Entity.Create(...)` static factory, private constructor
- Central DB entities: extend `Entity` base (not `BaseTenantEntity` — recipes have no TenantId)
- EF configuration: `IEntityTypeConfiguration<T>` in separate configuration class
- Endpoint pattern: `IEndpoint` with static `Map()`, nested Request/Response records, nested RequestValidator
- Soft delete: IsDeleted flag + DeletedAt timestamp via base Entity class

### Integration Points
- `CentralDbContext`: Add `DbSet<IndustryRecipe>` and apply configuration
- `IronMonkey.Data/Configurations/`: New `IndustryRecipeConfiguration.cs`
- `IronMonkey.Data/Entities/`: New `IndustryRecipe.cs` entity
- `IronMonkey.Data/Entities/Tenant.cs`: Add AppliedRecipeId and AppliedRecipeVersion properties
- `IronMonkey.Data/Migrations/Central/`: New migration for IndustryRecipe table + Tenant columns
- `TenantProvisioningService`: Extend to accept recipe ID, load recipe, apply during seeding
- `IronMonkey.Tests/Integration/TenantProvisioningTests.cs`: Extend with recipe-based provisioning tests

</code_context>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches for recipe entity design and provisioning integration.

</specifics>

<deferred>
## Deferred Ideas

- Recipe upgrade/migration for existing tenants — explicitly deferred to v1.2 (per REQUIREMENTS.md Out of Scope)
- Admin API for recipe CRUD — Phase 8 scope (RADM-01, RADM-02, RADM-03)
- Recipe preview endpoint — Phase 8 scope (ONBD-02)
- Domain-specific recipe content (Automobile, Educational) — Phase 7 scope (RCNT-01, RCNT-02)
- Sample lead seeding within recipes — Phase 7 scope (RCNT-03)
- Recipe marketplace / community-contributed recipes — future milestone

</deferred>

---

*Phase: 06-recipe-data-model*
*Context gathered: 2026-03-26*
