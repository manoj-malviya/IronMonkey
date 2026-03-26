# Phase 8: Onboarding & Admin API - Context

**Gathered:** 2026-03-26
**Status:** Ready for planning

<domain>
## Phase Boundary

Wire recipe selection into tenant signup, expose recipe preview and management endpoints. Users can select a recipe at signup with a preview of what it includes, and administrators can manage the recipe catalog via API. Does not include frontend/Blazor UI, recipe upgrade tooling, or new recipe content.

</domain>

<decisions>
## Implementation Decisions

### Signup-to-Recipe Flow
- **D-01:** Recipe selection happens at signup time. `POST /auth/signup` gains an optional `RecipeId` (Guid?) field. The chosen recipe ID is stored on SignupRequest and used during provisioning
- **D-02:** Replace the existing `IndustryType` (string) field on SignupRequest with `RecipeId` (Guid?). Clean break — recipe selection is the new industry selection. Requires a migration to drop IndustryType and add RecipeId
- **D-03:** Recipe selection is optional during signup. If RecipeId is null, provisioning defaults to the Blank recipe (fixed GUID `00000000-0000-0000-0000-000000000001`)
- **D-04:** Provisioning remains a separate manual admin action (existing approve -> provision flow unchanged). ProvisionTenantEndpoint reads RecipeId from the SignupRequest and passes it to TenantProvisioningService

### Recipe API Design
- **D-05:** All recipe endpoints under `/api/recipes` route group. RESTful: GET (list), GET /{id} (preview), POST (create), PUT /{id} (update), DELETE /{id} (deactivate)
- **D-06:** List endpoint (ONBD-05) returns metadata + counts: name, description, iconIdentifier, industrySlug, isBlank, version, plus stageCount, fieldCount, ruleCount, roleCount. Enough for a selection card without full content
- **D-07:** Preview endpoint (ONBD-02) returns full deserialized recipe content: stages[], fields[], rules[], roles[], sampleLeads[]. Everything needed to show "what you'll get" before committing
- **D-08:** Recipe update (RADM-02) uses full replacement via PUT. Matches existing `IndustryRecipe.UpdateContent()` which does full content replacement. Version auto-increments on each update
- **D-09:** Deactivate (RADM-03) uses DELETE which calls `IndustryRecipe.Deactivate()` — sets IsActive=false. Recipe remains in DB for audit trail

### Authorization Model
- **D-10:** Recipe list and preview endpoints (GET /api/recipes, GET /api/recipes/{id}) are AllowAnonymous. Required for signup flow where user is not yet authenticated. Recipes are platform templates, not sensitive data
- **D-11:** Recipe admin endpoints (POST, PUT, DELETE /api/recipes) require platform admin authorization (SuperAdmin role). Recipes are platform-level templates — tenant users cannot modify the catalog

### Error Handling & Edge Cases
- **D-12:** Provisioning with a deactivated recipe is blocked. Return validation error — admin must reactivate or change the SignupRequest's RecipeId before provisioning
- **D-13:** IndustrySlug enforced as unique across active recipes (unique index already exists from Phase 6 IndustryRecipeConfiguration)
- **D-14:** Recipe content is validated on create/update using FluentValidation. Validate structure: valid stage types (Entry/Active/ClosedWon/ClosedLost), non-empty names, valid field types (Text/Number/Dropdown/Boolean/Date), etc. Prevents broken recipes from failing at provisioning time

### Claude's Discretion
- Exact FluentValidation rules for recipe content structure validation depth
- Request/Response DTO design for each endpoint (nested records pattern)
- How to compute stageCount/fieldCount/ruleCount/roleCount for list response (deserialize ContentJson or store as computed columns)
- Migration naming and structure for SignupRequest IndustryType->RecipeId change
- Test structure for recipe API endpoints
- Whether to add a RecipeId to the ProvisionTenantEndpoint request body as an override, or always read from SignupRequest

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Onboarding requirements
- `.planning/REQUIREMENTS.md` -- ONBD-01 through ONBD-05 and RADM-01 through RADM-03 definitions
- `.planning/ROADMAP.md` -- Phase 8 success criteria (4 items) defining what must be TRUE

### Upstream phase artifacts
- `.planning/phases/06-recipe-data-model/06-CONTEXT.md` -- All 15 recipe infrastructure decisions (entity structure, application strategy, versioning, Blank recipe design)
- `.planning/phases/07-recipe-content/07-CONTEXT.md` -- Recipe content decisions (domain recipes, sample leads, seeding strategy)

### Key existing code references
- `IronMonkey.Data/Entities/IndustryRecipe.cs` -- Recipe entity with Create(), UpdateContent(), Deactivate() methods
- `IronMonkey.Data/RecipeContent/RecipeContentModel.cs` -- Recipe content DTO structure (PipelineStageDefinition, CustomFieldDefinitionDto, WorkflowRuleDefinition, RoleDefinition, SampleLeadDefinition)
- `IronMonkey.Data/Entities/SignupRequest.cs` -- Current entity with IndustryType string field to be replaced with RecipeId
- `IronMonkey.Data/Configurations/SignupRequestConfiguration.cs` -- EF config for SignupRequest (needs migration for field change)
- `IronMonkey.Data/Configurations/IndustryRecipeConfiguration.cs` -- EF config with unique index on IndustrySlug
- `IronMonkey.ApiService/Authentication/Endpoints/SignupRequestEndpoint.cs` -- POST /auth/signup handler to modify (add RecipeId to request)
- `IronMonkey.ApiService/Authentication/Endpoints/ApproveTenantEndpoint.cs` -- Approval flow (unchanged)
- `IronMonkey.ApiService/Authentication/Endpoints/ProvisionTenantEndpoint.cs` -- Provisioning trigger (needs to read RecipeId from SignupRequest and pass to service)
- `IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs` -- Already accepts optional recipeId param, needs deactivated-recipe check
- `IronMonkey.ApiService/Endpoints.cs` -- Master endpoint registration (add MapRecipeEndpoints group)
- `IronMonkey.Data/CentralDbContext.cs` -- DbSet<IndustryRecipe> already registered

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `IndustryRecipe` entity — already has Create(), UpdateContent(), Deactivate() methods ready for CRUD endpoints
- `TenantProvisioningService.ProvisionTenantAsync` — already accepts optional recipeId, just needs deactivated-recipe guard
- `RecipeContentModel` — fully typed DTO for deserialization, can be returned directly from preview endpoint
- Existing endpoint patterns (SignupRequestEndpoint, ApproveTenantEndpoint) — template for new recipe endpoints
- FluentValidation nested validator pattern — template for recipe content validation

### Established Patterns
- Endpoint pattern: `IEndpoint` with static `Map()`, nested Request/Response records, nested RequestValidator
- Route groups: `MapGroup("/api/recipes")` with `WithTags()`, auth policies
- Anonymous endpoints: `AllowAnonymous()` on route group (used in signup flow)
- Platform admin endpoints: `RequireAuthorization()` under `/admin` group
- Typed results: `Results<Ok<Response>, ValidationError, NotFound>` for clear error states

### Integration Points
- `SignupRequest.cs` — Drop IndustryType, add RecipeId (Guid?) property + migration
- `SignupRequestEndpoint.cs` — Add RecipeId to Request record and validator
- `ProvisionTenantEndpoint.cs` — Read RecipeId from SignupRequest, pass to provisioning service
- `TenantProvisioningService.cs` — Add deactivated-recipe check before provisioning
- `Endpoints.cs` — Add `MapRecipeEndpoints()` method with new recipe endpoint registrations
- `ConfigureServices.cs` — No new service registrations expected (recipe CRUD uses CentralDbContext directly)

</code_context>

<specifics>
## Specific Ideas

No specific requirements -- open to standard approaches following existing endpoint patterns.

</specifics>

<deferred>
## Deferred Ideas

- Recipe upgrade/migration for existing tenants -- v1.2 (Out of Scope per REQUIREMENTS.md)
- Blazor frontend for recipe selection UI -- future phase (frontend not yet wired to Phase 2-5 endpoints)
- Auto-provisioning on approval (Hangfire job) -- future enhancement if manual provisioning becomes bottleneck
- Recipe marketplace / community-contributed recipes -- future milestone
- Recipe export/import (JSON file) -- future milestone

</deferred>

---

*Phase: 08-onboarding-admin-api*
*Context gathered: 2026-03-26*
