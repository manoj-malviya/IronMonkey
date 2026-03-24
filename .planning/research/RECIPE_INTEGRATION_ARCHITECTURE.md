# Recipe Integration Architecture Patterns

**Project:** IronMonkey v1.1 (Industry Recipe Onboarding)
**Researched:** 2026-03-24
**Confidence:** HIGH (verified against existing v1.0 architecture)
**Focus:** Integration points, new/modified components, build order

## Executive Summary

Industry recipes integrate at **four key architectural points** in the existing multi-tenant system:

1. **CentralDbContext** — New recipe template storage (5 new entities)
2. **SignupRequest** — Recipe selection during signup (2 new fields)
3. **TenantProvisioningService** — Recipe application during provisioning (1 new method)
4. **API Layer** — Recipe listing/discovery endpoints (2-3 new endpoints)

The integration maintains **minimal changes** to existing code. No schema modifications to TenantDbContext. All recipe data uses existing entity types (PipelineStage, CustomFieldDefinition, WorkflowRule). Recipes remain data-driven templates, not code changes.

## Integration Points Map

```
┌─────────────────────────────────────────────────────────────────────┐
│  STARTUP: Load Recipes from CentralDb (happens once at app start)   │
└────────────────────────┬────────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────────┐
│  SIGNUP: User selects recipe when registering                       │
│  POST /auth/signup { ..., recipeId }                                │
│  → SignupRequest.SelectedRecipeId saved in CentralDb                │
└────────────────────────┬────────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────────┐
│  APPROVAL: Admin reviews tenant & can override recipe choice        │
│  PUT /admin/signup/{id}/approve { ..., recipeIdOverride? }          │
│  → SignupRequest.SelectedRecipeId updated (if override provided)    │
└────────────────────────┬────────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────────┐
│  PROVISIONING: Recipe applied during tenant database setup          │
│  TenantProvisioningService.ProvisionTenantAsync(signupRequestId)    │
│  ├─ Step 1: Create Tenant record in CentralDb                      │
│  ├─ Step 2: Create isolated PostgreSQL database                    │
│  ├─ Step 3: Run EF migrations (schema setup)                       │
│  ├─ Step 4: [NEW] ApplyRecipeAsync(selectedRecipeId)              │
│  │           ├─ Load Recipe from CentralDb                         │
│  │           ├─ For each stage: PipelineStage.Create() → TenantDb │
│  │           ├─ For each field: CustomFieldDefinition.Create()    │
│  │           └─ For each workflow: WorkflowRule.Create()          │
│  ├─ Step 5: Seed Admin user                                       │
│  └─ Step 6: Mark tenant as provisioned                            │
└─────────────────────────────────────────────────────────────────────┘
```

## New vs. Modified Components Table

### NEW COMPONENTS (Total: 8 additions)

| Component | Type | Location | Responsibility |
|-----------|------|----------|-----------------|
| **IndustryRecipe** | Entity | IronMonkey.Data/Entities | Recipe template definition (name, description, metadata) |
| **RecipeStageDefinition** | Entity | IronMonkey.Data/Entities | Stage templates within a recipe (e.g., "Lead", "Qualified", "Sold") |
| **RecipeFieldDefinition** | Entity | IronMonkey.Data/Entities | Custom field templates within a recipe (e.g., "VIN", "Model Year") |
| **RecipeWorkflowDefinition** | Entity | IronMonkey.Data/Entities | Workflow/rule templates within a recipe (auto-assign, notify, etc.) |
| **RecipeRoleDefinition** | Entity | IronMonkey.Data/Entities | Role templates within a recipe (future: "Sales Manager", "Closer") |
| **IndustryRecipeConfiguration** | EF Config | IronMonkey.Data/Configurations | Entity type configuration (keys, indexes, relationships) |
| **ListRecipesEndpoint** | Endpoint | IronMonkey.ApiService/Features/Recipes/Endpoints | GET /recipes — list available recipes for signup UI |
| **RecipeApplicationService** | Service | IronMonkey.ApiService/Features/Recipes/Services | Applies recipe definitions to provisioned tenant database |

### MODIFIED COMPONENTS (Total: 5 changes)

| Component | Changes | Impact | Risk |
|-----------|---------|--------|------|
| **SignupRequest** | Add `SelectedRecipeId` (Guid?) + `SelectedRecipeName` (string) | Stores recipe choice through approval workflow | **LOW** — additive only, backward compatible |
| **SignupRequestEndpoint** | Include `recipeId` in POST request; validate recipe exists | User can select recipe during signup | **LOW** — validation additive |
| **TenantProvisioningService** | Add `ApplyRecipeAsync()` method; call after migrations | Recipes applied during provisioning | **MEDIUM** — orchestration change, but isolated in new method |
| **ApproveSignupRequestEndpoint** | Allow `recipeIdOverride` in PUT request | Admin can change recipe during approval | **LOW** — optional override parameter |
| **CentralDbContext** | Add DbSet<IndustryRecipe>, DbSet<Recipe*Definition> (4 more) | Persist recipe templates | **LOW** — additive only |

### UNCHANGED COMPONENTS (Verified No Impact)

| Component | Why No Change |
|-----------|---------------|
| **TenantDbContext** | Recipes apply using existing entity types; schema unchanged |
| **PipelineStage** | Recipe uses existing Create() factory; no modifications |
| **CustomFieldDefinition** | Recipe uses existing Create() factory; no modifications |
| **WorkflowRule** | Recipe uses existing Create() factory; no modifications |
| **Role** | Recipe uses existing entities; no modifications |
| **User** | Admin user seeded via existing pattern; unchanged |
| **Lead, Contact, Opportunity** | Recipe doesn't touch lead schema; completely unchanged |
| **Database migrations** | Tenant DB schema identical; only central DB gains recipe tables |

## Data Flow — Detailed Integration

### Integration Point 1: Startup/Initialization

**When:** Application starts (one-time)

**What happens:**
```
AppStartup
└─ DependencyInjection (ConfigureServices.cs)
   └─ Register RecipeApplicationService
   └─ (Optional) Load recipes into memory cache for fast lookup during signup
```

**Code location:** `/IronMonkey.ApiService/ConfigureServices.cs`

**New code:** Service registration, optional caching logic

**Existing code:** No changes needed

---

### Integration Point 2: Signup Flow

**When:** User registers at `/auth/signup`

**Request (modified):**
```json
{
  "companyName": "Acme Auto Sales",
  "adminEmail": "owner@acme.com",
  "adminPassword": "SecurePass123!",
  "phone": "555-0100",
  "industryType": "Automotive",
  "companySize": "50-100",
  "address": "123 Main St",
  "billingContact": "billing@acme.com",
  "recipeId": "550e8400-e29b-41d4-a716-446655440000"  // NEW
}
```

**Flow:**
```
POST /auth/signup
├─ Validate request (existing validators)
├─ [NEW] Validate recipeId exists in CentralDb.IndustryRecipes
├─ Hash admin password (existing)
├─ Create SignupRequest with:
│  ├─ All existing fields (company, email, etc.)
│  ├─ SelectedRecipeId (new)
│  └─ SelectedRecipeName (new, from recipe.Name)
├─ Persist to CentralDb.SignupRequests
└─ Return 201 with SignupRequestId
```

**Code locations:**
- `/IronMonkey.ApiService/Authentication/Endpoints/SignupRequestEndpoint.cs` (modified)
- `/IronMonkey.Data/Entities/SignupRequest.cs` (modified)

**New code in SignupRequestEndpoint:**
```csharp
// Validate recipe exists (if provided)
if (request.RecipeId.HasValue && request.RecipeId.Value != Guid.Empty)
{
    var recipeExists = await centralDb.IndustryRecipes
        .AnyAsync(r => r.Id == request.RecipeId && r.IsActive);
    if (!recipeExists)
        return new ValidationError($"Recipe {request.RecipeId} not found or inactive.");
}
```

---

### Integration Point 3: Recipe Listing (Signup UI Support)

**When:** Frontend needs to show list of available recipes for user selection

**Endpoint (new):**
```
GET /recipes
├─ Query CentralDb.IndustryRecipes (IsActive = true)
├─ Return: [ { id, name, description, industryCategory, displayOrder } ]
├─ Cache: Optional in-memory for 1 hour (avoid repeated DB hits)
└─ Visibility: Anonymous (no auth required)
```

**Code locations:**
- `/IronMonkey.ApiService/Features/Recipes/Endpoints/ListRecipesEndpoint.cs` (new)

**Response example:**
```json
[
  {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "name": "Automobile Dealership",
    "description": "Optimized for auto sales with stages: Lead, Test Drive, Sold, Lost",
    "industryCategory": "Automotive",
    "displayOrder": 1,
    "stageCount": 5,
    "fieldCount": 12
  },
  {
    "id": "550e8400-e29b-41d4-a716-446655440001",
    "name": "Educational Institution",
    "description": "For schools and universities: Inquiry, Engaged, Applied, Enrolled, Declined",
    "industryCategory": "Education",
    "displayOrder": 2,
    "stageCount": 5,
    "fieldCount": 8
  },
  {
    "id": "00000000-0000-0000-0000-000000000000",
    "name": "Blank/Custom",
    "description": "Start with empty configuration; build your own stages and fields",
    "industryCategory": "Custom",
    "displayOrder": 999,
    "stageCount": 0,
    "fieldCount": 0
  }
]
```

---

### Integration Point 4: Admin Approval & Recipe Override

**When:** Admin reviews signup request and decides whether to approve/reject

**Endpoint (modified):**
```
PUT /admin/signup/{id}/approve
├─ Existing validation: check SignupRequest exists and status = Pending
├─ [NEW] Optional: accept recipeIdOverride parameter
├─ If recipeIdOverride provided:
│  ├─ Validate recipe exists
│  └─ Update SignupRequest.SelectedRecipeId
├─ Set SignupRequest.Status = "Approved"
├─ Trigger provisioning: ProvisionTenantAsync(signupRequestId)
└─ Return 200 with Tenant details
```

**Code locations:**
- `/IronMonkey.ApiService/Authentication/Endpoints/ApproveSignupRequestEndpoint.cs` (modified)

**New logic:**
```csharp
if (!string.IsNullOrEmpty(request.RecipeIdOverride))
{
    var recipe = await centralDb.IndustryRecipes
        .SingleOrDefaultAsync(r => r.Id == request.RecipeIdOverride && r.IsActive)
        ?? throw new InvalidOperationException("Recipe override not found");

    signupRequest.SelectRecipe(recipe.Id, recipe.Name);
}
```

---

### Integration Point 5: Provisioning & Recipe Application (CORE)

**When:** Admin approves signup → TenantProvisioningService.ProvisionTenantAsync() runs

**Current flow (existing):**
```
ProvisionTenantAsync(signupRequestId)
├─ Step 1: Load SignupRequest
├─ Step 2: Create Tenant record
├─ Step 3: Create PostgreSQL database
├─ Step 4: Run EF migrations
├─ Step 5: Seed default data (Admin role + admin user)
└─ Step 6: Mark tenant as provisioned
```

**New flow (with recipe):**
```
ProvisionTenantAsync(signupRequestId)
├─ Step 1: Load SignupRequest
├─ Step 2: Create Tenant record
├─ Step 3: Create PostgreSQL database
├─ Step 4: Run EF migrations
├─ Step 5: [NEW] ApplyRecipeAsync(signupRequest.SelectedRecipeId, tenant.Id)
│          ├─ Load recipe from CentralDb
│          ├─ For each RecipeStageDefinition in recipe:
│          │  └─ Create PipelineStage in TenantDb
│          ├─ For each RecipeFieldDefinition in recipe:
│          │  └─ Create CustomFieldDefinition in TenantDb
│          ├─ For each RecipeWorkflowDefinition in recipe:
│          │  └─ Create WorkflowRule in TenantDb
│          └─ SaveChangesAsync()
├─ Step 6: Seed default data (Admin role + admin user)
└─ Step 7: Mark tenant as provisioned
```

**Code locations:**
- `/IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs` (modified)
- `/IronMonkey.ApiService/Features/Recipes/Services/RecipeApplicationService.cs` (new)

**ApplyRecipeAsync() pseudocode:**
```csharp
private async Task ApplyRecipeAsync(Guid? recipeId, Guid tenantId, CancellationToken ct)
{
    // Handle null/blank recipe
    if (!recipeId.HasValue || recipeId == Guid.Empty)
        return;  // No recipe to apply

    // Load recipe from central DB
    var recipe = await _centralDb.IndustryRecipes
        .Include(r => r.Stages)
        .Include(r => r.Fields)
        .Include(r => r.Workflows)
        .SingleOrDefaultAsync(r => r.Id == recipeId, ct)
        ?? throw new InvalidOperationException($"Recipe {recipeId} not found");

    // Create PipelineStages
    foreach (var stageDef in recipe.Stages.OrderBy(s => s.Order))
    {
        var stage = PipelineStage.Create(
            tenantId,
            stageDef.StageName,
            stageDef.Order,
            Enum.Parse<StageType>(stageDef.StageType));
        _tenantDb.PipelineStages.Add(stage);
    }

    // Create CustomFieldDefinitions
    foreach (var fieldDef in recipe.Fields)
    {
        var field = CustomFieldDefinition.Create(
            tenantId,
            fieldDef.FieldName,
            Enum.Parse<CustomFieldType>(fieldDef.FieldType),
            fieldDef.IsRequired,
            fieldDef.Options);
        _tenantDb.CustomFieldDefinitions.Add(field);
    }

    // Create WorkflowRules
    foreach (var wfDef in recipe.Workflows.Where(w => w.IsActive))
    {
        var workflow = WorkflowRule.Create(
            tenantId,
            wfDef.RuleName,
            Enum.Parse<WorkflowTrigger>(wfDef.Trigger),
            wfDef.ConditionJson,
            wfDef.ActionJson);
        _tenantDb.WorkflowRules.Add(workflow);
    }

    await _tenantDb.SaveChangesAsync(ct);
}
```

## Build Order & Dependencies

### **Phase 1: Recipe Data Model** (Est. 2-3 days)
**Status:** Ready to start immediately. No dependencies.

**Deliverables:**
1. Create 5 new entity classes:
   - IndustryRecipe.cs
   - RecipeStageDefinition.cs
   - RecipeFieldDefinition.cs
   - RecipeWorkflowDefinition.cs
   - RecipeRoleDefinition.cs

2. Create EF configuration:
   - IndustryRecipeConfiguration.cs (includes relationships: Recipe.HasMany(Stages), etc.)

3. Create EF migration (Central DB):
   - Migration name: `AddIndustryRecipes`
   - Adds 5 new tables: IndustryRecipes, RecipeStageDefinitions, RecipeFieldDefinitions, RecipeWorkflowDefinitions, RecipeRoleDefinitions
   - Seed initial data: Automobile Dealership, Educational Institution, Blank/Custom recipes

4. Update CentralDbContext:
   - Add DbSet<IndustryRecipe>
   - Add DbSet<RecipeStageDefinition>
   - Add DbSet<RecipeFieldDefinition>
   - Add DbSet<RecipeWorkflowDefinition>
   - Add DbSet<RecipeRoleDefinition>
   - Call modelBuilder.ApplyConfiguration<IndustryRecipeConfiguration>()

**Key files:**
- `/IronMonkey.Data/Entities/IndustryRecipe.cs` (new)
- `/IronMonkey.Data/Entities/RecipeStageDefinition.cs` (new)
- `/IronMonkey.Data/Entities/RecipeFieldDefinition.cs` (new)
- `/IronMonkey.Data/Entities/RecipeWorkflowDefinition.cs` (new)
- `/IronMonkey.Data/Entities/RecipeRoleDefinition.cs` (new)
- `/IronMonkey.Data/Configurations/IndustryRecipeConfiguration.cs` (new)
- `/IronMonkey.Data/CentralDbContext.cs` (modified: add DbSets)
- `/IronMonkey.Data/Migrations/Central/[datetime]_AddIndustryRecipes.cs` (new)

**Testing:**
- Unit tests for entity factories
- Integration test: can create IndustryRecipe and seed definitions

---

### **Phase 2: Recipe API & Listing** (Est. 1-2 days)
**Depends on:** Phase 1 complete

**Deliverables:**
1. Create ListRecipesEndpoint:
   - GET /recipes
   - Return active recipes with metadata
   - Support optional caching

2. Create GetRecipeDetailEndpoint (optional, v1.1+):
   - GET /recipes/{id}
   - Return recipe with full stage/field/workflow definitions

3. Register endpoints in Endpoints.cs

**Key files:**
- `/IronMonkey.ApiService/Features/Recipes/Endpoints/ListRecipesEndpoint.cs` (new)
- `/IronMonkey.ApiService/Endpoints.cs` (modified: add MapEndpoint<ListRecipesEndpoint>())
- `/IronMonkey.Tests/Integration/RecipeEndpointsTests.cs` (new tests)

**Testing:**
- Integration test: GET /recipes returns active recipes
- Integration test: GET /recipes/{id} returns recipe details

---

### **Phase 3: Signup Integration & Recipe Selection** (Est. 1-2 days)
**Depends on:** Phase 1, Phase 2

**Deliverables:**
1. Update SignupRequest entity:
   - Add `SelectedRecipeId` (Guid?)
   - Add `SelectedRecipeName` (string)
   - Add `SelectRecipe(Guid recipeId, string recipeName)` method

2. Update SignupRequestEndpoint:
   - Add `recipeId` parameter to POST request
   - Validate recipe exists before creating SignupRequest
   - Store SelectedRecipeId + SelectedRecipeName in SignupRequest

3. Create EF migration (Central DB):
   - Migration name: `AddRecipeSelectionToSignupRequest`
   - Adds 2 columns to SignupRequests table: SelectedRecipeId, SelectedRecipeName

4. Update ApproveSignupRequestEndpoint:
   - Add optional `recipeIdOverride` parameter
   - Allow admin to change recipe before provisioning

**Key files:**
- `/IronMonkey.Data/Entities/SignupRequest.cs` (modified: add fields + SelectRecipe() method)
- `/IronMonkey.ApiService/Authentication/Endpoints/SignupRequestEndpoint.cs` (modified: add recipe validation)
- `/IronMonkey.ApiService/Authentication/Endpoints/ApproveSignupRequestEndpoint.cs` (modified: add override logic)
- `/IronMonkey.Data/Migrations/Central/[datetime]_AddRecipeSelectionToSignupRequest.cs` (new)
- `/IronMonkey.Tests/Integration/SignupWithRecipeTests.cs` (new tests)

**Testing:**
- Integration test: SignupRequest stores selected recipe
- Integration test: Admin can override recipe during approval

---

### **Phase 4: Provisioning & Recipe Application** (Est. 2-3 days)
**Depends on:** Phase 1, Phase 3 complete

**Deliverables:**
1. Create RecipeApplicationService:
   - Method: `ApplyRecipeAsync(Guid recipeId, Guid tenantId, TenantDbContext tenantDb)`
   - Deserialize recipe stages/fields/workflows from CentralDb
   - Create corresponding TenantDb entities via factory methods
   - Handle null/Guid.Empty recipe (no-op)

2. Integrate into TenantProvisioningService:
   - Call ApplyRecipeAsync() after migrations, before admin user seeding
   - Pass tenant's SelectedRecipeId from SignupRequest

3. Update TenantProvisioningService constructor:
   - Accept IRecipeApplicationService dependency (injected)

**Key files:**
- `/IronMonkey.ApiService/Features/Recipes/Services/RecipeApplicationService.cs` (new)
- `/IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs` (modified: call ApplyRecipeAsync)
- `/IronMonkey.ApiService/ConfigureServices.cs` (modified: register RecipeApplicationService)
- `/IronMonkey.Tests/Integration/TenantProvisioningWithRecipeTests.cs` (new tests)

**Testing:**
- Integration test: Full provisioning flow with recipe seeding
- Integration test: Verify stages created with correct order
- Integration test: Verify custom fields created with correct type/options
- Integration test: Verify workflows created and active

---

### **Phase 5: Admin Recipe Management** (Future, v1.2+)
**Depends on:** Phase 1

**Deliverables (deferred):**
1. CreateRecipeEndpoint (POST /admin/recipes)
2. UpdateRecipeEndpoint (PUT /admin/recipes/{id})
3. DeleteRecipeEndpoint (DELETE /admin/recipes/{id})
4. Blazor UI for recipe editing

---

## Critical Sequencing Rules

**DO NOT start Phase 3 before Phase 1 is complete.**
- Phase 3 needs IndustryRecipe entities in CentralDb to validate against

**DO NOT start Phase 4 before Phase 3 is complete.**
- Phase 4 reads SignupRequest.SelectedRecipeId (must exist in schema)

**Phase 2 can run in parallel with Phase 1**, but endpoints won't work until Phase 1 migration runs.

**Optimal parallel work:**
- Developer A: Phase 1 (entities, migration, seed data)
- Developer B: Phase 2 (endpoints) — wait for Phase 1 migration to run
- Developer C: Phase 3 (signup entity, validation)
- Then: Phase 4 (provisioning integration) — requires all above

## Database Migration Strategy

### Central DB Migrations (in order)

1. **Initial (v1.0):** Tenants, SignupRequests, UserTenantIndex, ApiKeys, WebForms
2. **Recipe Model (Phase 1):** IndustryRecipes, RecipeStageDefinitions, RecipeFieldDefinitions, RecipeWorkflowDefinitions, RecipeRoleDefinitions + seed data
3. **Recipe Selection (Phase 3):** Add SelectedRecipeId, SelectedRecipeName columns to SignupRequests

### Tenant DB Migrations (unchanged)

- Apply same migrations (schema setup) to all tenant databases
- No tenant-specific recipe data in TenantDbContext (recipes are applied via application layer)
- Recipe data appears as regular PipelineStage, CustomFieldDefinition, WorkflowRule entities (no recipe metadata stored)

## Risk Mitigation Strategies

| Risk | Mitigation |
|------|-----------|
| **Recipe data loss on DB failure** | Recipes stored in CentralDb with standard backup strategy. Immutable after seed. |
| **Provisioning fails mid-recipe** | Use transaction for ApplyRecipeAsync(). If rollback, no partial data left in TenantDb. |
| **Admin selects invalid recipe** | Validation on API layer; can't select inactive/deleted recipe. |
| **Recipe seeding too slow** | Batch insert ~100 entities per recipe. Monitor in Phase 4 integration tests. If slow, defer to async job. |
| **Tenant modifies recipe-seeded data** | Expected behavior. Recipes are starting points. No sync backward to recipe. |
| **Schema changes break recipe application** | Use unit tests to verify deserialization + entity creation in Phase 4. |

## Summary Table: Integration Points

| Point | Location | Trigger | Data Flow | Risk Level |
|-------|----------|---------|-----------|-----------|
| **1. Initialization** | App startup | App starts | Load recipes into memory | LOW |
| **2. Signup** | POST /auth/signup | User submits form | Store selected recipe ID | LOW |
| **3. Listing** | GET /recipes | Signup UI loads | Query recipes from CentralDb | LOW |
| **4. Approval** | PUT /admin/signup/{id}/approve | Admin approves | Override recipe if desired | LOW |
| **5. Provisioning** | TenantProvisioningService | Approval triggered | Apply recipe to tenant DB | MEDIUM |

## Sources & Confidence

| Source | Confidence | Why |
|--------|-----------|-----|
| IronMonkey v1.0 architecture | HIGH | Existing patterns proven in production |
| EF Core 10.0.5 documentation | HIGH | Factory patterns, entity creation verified |
| PostgreSQL JSONB support | HIGH | v1.0 uses for custom fields |
| SaaS onboarding patterns (AWS, Stripe) | MEDIUM | Industry best practices, adapted to v1.0 |
| Community examples (GitHub) | MEDIUM | Similar multi-tenant recipe implementations |

