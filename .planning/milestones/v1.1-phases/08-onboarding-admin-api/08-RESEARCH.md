# Phase 8: Onboarding & Admin API - Research

**Researched:** 2026-03-26
**Domain:** API endpoint design, multi-tenant recipe provisioning, platform admin authorization
**Confidence:** HIGH

## Summary

Phase 8 integrates recipe selection into the tenant signup and provisioning flow, then exposes recipe management via API to platform administrators. The foundation is solid: IndustryRecipe entity (Phase 6), recipe content (Phase 7), TenantProvisioningService, and existing endpoint patterns are all in place. The phase requires straightforward changes: add RecipeId to SignupRequest, implement 5 recipe API endpoints (GET list, GET preview, POST create, PUT update, DELETE deactivate), and add authorization/validation guards.

**Primary recommendation:** Follow established endpoint patterns (`IEndpoint` interface, nested Request/Response records, FluentValidation) for recipe CRUD; reuse IndustryRecipe factory methods (Create, UpdateContent, Deactivate); guard provisioning against deactivated recipes; make list and preview endpoints anonymous (required for unauthenticated signup), restrict admin endpoints to platform SuperAdmin role.

---

## User Constraints (from CONTEXT.md)

### Locked Decisions

**Signup-to-Recipe Flow**
- Recipe selection happens at signup time via optional `RecipeId` field on POST /auth/signup
- `IndustryType` (string) on SignupRequest is replaced with `RecipeId` (Guid?), cleaned with a migration
- Recipe selection is optional; if null, provisioning defaults to Blank recipe (fixed GUID `00000000-0000-0000-0000-000000000001`)
- Provisioning remains a separate manual admin action; ProvisionTenantEndpoint reads RecipeId from SignupRequest

**Recipe API Design**
- All recipe endpoints under `/api/recipes` route group, RESTful: GET (list), GET /{id} (preview), POST (create), PUT /{id} (update), DELETE /{id} (deactivate)
- List endpoint returns metadata + counts: name, description, iconIdentifier, industrySlug, isBlank, version, plus stageCount, fieldCount, ruleCount, roleCount
- Preview endpoint returns full deserialized recipe content: stages[], fields[], rules[], roles[], sampleLeads[]
- Recipe update uses full replacement via PUT; version auto-increments
- Deactivate uses DELETE which calls `IndustryRecipe.Deactivate()` — sets IsActive=false

**Authorization Model**
- Recipe list and preview endpoints are AllowAnonymous (required for signup flow)
- Recipes are platform templates, not sensitive data
- Recipe admin endpoints (POST, PUT, DELETE) require platform admin authorization (SuperAdmin role)

**Error Handling & Edge Cases**
- Provisioning with a deactivated recipe is blocked; return validation error
- IndustrySlug enforced as unique across active recipes
- Recipe content is validated on create/update using FluentValidation

### Claude's Discretion

- Exact FluentValidation rules for recipe content structure validation depth
- Request/Response DTO design for each endpoint (nested records pattern)
- How to compute stageCount/fieldCount/ruleCount/roleCount for list response (deserialize ContentJson or store as computed columns)
- Migration naming and structure for SignupRequest IndustryType->RecipeId change
- Test structure for recipe API endpoints
- Whether to add a RecipeId to the ProvisionTenantEndpoint request body as an override, or always read from SignupRequest

### Deferred Ideas (OUT OF SCOPE)

- Recipe upgrade/migration for existing tenants — v1.2
- Blazor frontend for recipe selection UI — future phase
- Auto-provisioning on approval (Hangfire job) — future enhancement
- Recipe marketplace / community-contributed recipes — future milestone
- Recipe export/import (JSON file) — future milestone

---

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| ONBD-01 | User can select an industry recipe during tenant signup | SignupRequest.RecipeId added via migration, SignupRequestEndpoint.Request gains RecipeId field, optional during signup |
| ONBD-02 | User can preview what a recipe includes (stages, fields, rules) before committing | GET /api/recipes/{id} endpoint returns full RecipeContentModel (stages, fields, rules, roles, sampleLeads) |
| ONBD-03 | Tenant provisioning atomically applies the selected recipe | TenantProvisioningService.ProvisionTenantAsync already accepts optional recipeId; reads from SignupRequest; SeedTenantDataAsync applies content in single transaction |
| ONBD-04 | All recipe-seeded configuration is fully modifiable by tenant after provisioning | Recipes are copied to tenant DB at provisioning (no FK back to template); tenant can modify all entities |
| ONBD-05 | System provides a list of available recipes via API endpoint | GET /api/recipes endpoint returns minimal metadata + counts for each active recipe |
| RADM-01 | Admin can create new industry recipes via API | POST /api/recipes endpoint creates IndustryRecipe via entity factory, requires SuperAdmin authorization |
| RADM-02 | Admin can update existing recipe definitions via API | PUT /api/recipes/{id} endpoint calls IndustryRecipe.UpdateContent(), version auto-increments |
| RADM-03 | Admin can deactivate (soft-delete) a recipe so it no longer appears in the selection list | DELETE /api/recipes/{id} endpoint calls IndustryRecipe.Deactivate(), sets IsActive=false |

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| ASP.NET Core Minimal API | .NET 10.0 | Endpoint routing | Lightweight, composable, used across all existing endpoints |
| Entity Framework Core | 10.0.5 | ORM, recipe CRUD | Existing pattern for all data access; IndustryRecipe already mapped in CentralDbContext |
| FluentValidation | (implicit) | Request validation | Global validator registration in ConfigureServices; nested RequestValidator pattern established |
| System.Text.Json | (built-in) | Recipe content serialization | Serializes/deserializes RecipeContentModel to/from ContentJson JSONB column; no camelCase conversion (uses PascalCase keys) |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| xUnit 2.9.3 | 2.9.3 | Endpoint integration tests | Testing recipe CRUD endpoints with real central DB; matches existing test suite |
| PostgreSQL + Npgsql | 10.0.1 | Central database | IndustryRecipe stored in central DB; unique index on IndustrySlug already exists |

---

## Architecture Patterns

### Recommended Project Structure

New files:
```
IronMonkey.ApiService/
├── Features/Recipes/
│   ├── RecipeListEndpoint.cs        # GET /api/recipes (anonymous)
│   ├── RecipePreviewEndpoint.cs     # GET /api/recipes/{id} (anonymous)
│   ├── CreateRecipeEndpoint.cs      # POST /api/recipes (SuperAdmin)
│   ├── UpdateRecipeEndpoint.cs      # PUT /api/recipes/{id} (SuperAdmin)
│   └── DeactivateRecipeEndpoint.cs  # DELETE /api/recipes/{id} (SuperAdmin)
└── [no new services needed — CRUD uses CentralDbContext directly]

IronMonkey.Data/
└── [Migrations/Central/] — new migration for SignupRequest (drop IndustryType, add RecipeId)

IronMonkey.Tests/Integration/
└── RecipeEndpointTests.cs           # Integration tests for all 5 endpoints
```

Modified files:
```
IronMonkey.Data/
├── Entities/SignupRequest.cs        # Replace IndustryType with RecipeId
└── Migrations/Central/             # Add new migration: rename IndustryType → RecipeId

IronMonkey.ApiService/
├── Authentication/Endpoints/SignupRequestEndpoint.cs    # Add RecipeId to Request
├── Authentication/Endpoints/ProvisionTenantEndpoint.cs  # Read RecipeId from SignupRequest, pass to service
├── Authentication/Services/TenantProvisioningService.cs # Add deactivated-recipe guard
└── Endpoints.cs                                          # Add MapRecipeEndpoints() method
```

### Pattern 1: Anonymous Recipe List Endpoint

**What:** GET /api/recipes returns active recipes with metadata and field counts (for signup UI selection)

**When to use:** Unauthenticated users browsing available recipes during signup

**Example:**
```csharp
// Source: Established endpoint pattern from SignupRequestEndpoint
public class RecipeListEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/recipes", Handle)
        .WithSummary("List available recipes for signup selection")
        .WithTags("Recipes")
        .AllowAnonymous();

    public record Response(
        Guid Id,
        string Name,
        string Description,
        string IconIdentifier,
        string IndustrySlug,
        bool IsBlank,
        int Version,
        int StageCount,
        int FieldCount,
        int RuleCount,
        int RoleCount);

    private static async Task<Ok<List<Response>>> Handle(
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var recipes = await centralDb.IndustryRecipes
            .AsNoTracking()
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.IsBlank)  // Blank recipe first
            .ThenBy(r => r.Name)
            .ToListAsync(cancellationToken);

        var responses = recipes.Select(r =>
        {
            var content = System.Text.Json.JsonSerializer.Deserialize<RecipeContentModel>(r.ContentJson) ?? new();
            return new Response(
                r.Id, r.Name, r.Description, r.IconIdentifier, r.IndustrySlug, r.IsBlank, r.Version,
                content.PipelineStages.Count,
                content.CustomFields.Count,
                content.WorkflowRules.Count,
                content.Roles.Count);
        }).ToList();

        return TypedResults.Ok(responses);
    }
}
```

### Pattern 2: Recipe Preview Endpoint (Full Content)

**What:** GET /api/recipes/{id} returns complete recipe structure (stages, fields, rules, roles, sample leads)

**When to use:** Authenticated or unauthenticated user wants to see "what you get" before selecting during signup

**Example:**
```csharp
public class RecipePreviewEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/recipes/{id}", Handle)
        .WithSummary("Preview full contents of a recipe")
        .WithTags("Recipes")
        .AllowAnonymous();

    public record Response(
        Guid Id,
        string Name,
        RecipeContentModel Content);

    private static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var recipe = await centralDb.IndustryRecipes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.IsActive, cancellationToken);

        if (recipe is null)
            return TypedResults.NotFound();

        var content = System.Text.Json.JsonSerializer.Deserialize<RecipeContentModel>(recipe.ContentJson) ?? new();
        return TypedResults.Ok(new Response(recipe.Id, recipe.Name, content));
    }
}
```

### Pattern 3: Create Recipe Endpoint (SuperAdmin Only)

**What:** POST /api/recipes creates a new recipe; requires platform admin

**When to use:** Platform admin adds new industry templates to the catalog

**Example:**
```csharp
public class CreateRecipeEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/recipes", Handle)
        .WithSummary("Create a new recipe")
        .WithTags("Platform Admin")
        .RequireAuthorization()
        .WithRequestValidation<Request>();

    public record Request(
        string Name,
        string Description,
        string IconIdentifier,
        string IndustrySlug,
        bool IsBlank,
        RecipeContentModel Content);

    public record Response(Guid RecipeId, string Message);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
            RuleFor(x => x.IconIdentifier).NotEmpty().MaximumLength(100);
            RuleFor(x => x.IndustrySlug).NotEmpty().MaximumLength(100).Matches(@"^[a-z0-9\-]+$");
            RuleFor(x => x.Content).SetValidator(new RecipeContentValidator());
        }
    }

    private static async Task<Results<Created<Response>, ValidationError>> Handle(
        Request request,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        // Verify IndustrySlug is unique among active recipes
        var slugExists = await centralDb.IndustryRecipes
            .AnyAsync(r => r.IndustrySlug == request.IndustrySlug && r.IsActive, cancellationToken);

        if (slugExists)
            return new ValidationError("IndustrySlug must be unique among active recipes.");

        var recipe = IndustryRecipe.Create(
            request.Name,
            request.Description,
            request.IndustrySlug,
            request.IconIdentifier,
            request.IsBlank,
            request.Content);

        centralDb.IndustryRecipes.Add(recipe);
        await centralDb.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/recipes/{recipe.Id}", new Response(recipe.Id, "Recipe created successfully."));
    }
}
```

### Pattern 4: Deactivated Recipe Guard in Provisioning

**What:** TenantProvisioningService blocks provisioning if the recipe is deactivated

**When to use:** Before applying recipe content, verify IsActive=true

**Example:**
```csharp
// In TenantProvisioningService.ProvisionTenantAsync (Step 5)
if (recipeId.HasValue)
{
    var recipe = await _centralDb.IndustryRecipes
        .AsNoTracking()
        .FirstOrDefaultAsync(r => r.Id == recipeId.Value, cancellationToken)
        ?? throw new InvalidOperationException($"Recipe {recipeId} not found.");

    if (!recipe.IsActive)
        throw new InvalidOperationException($"Cannot provision with deactivated recipe {recipe.Name}. Reactivate the recipe or select a different one.");

    recipeId = recipe.Id; // Confirmed active, proceed
}
```

### Anti-Patterns to Avoid

- **Computing field counts on every list request:** Deserializing ContentJson for 20+ recipes on each request is expensive. Prefer storing computed counts as columns (deferred to Claude's Discretion) or caching the response.
- **Allowing tenant-level users to modify recipe catalog:** Recipe endpoints require SuperAdmin only. Tenants can modify seeded data in their own workspace after provisioning, but cannot edit the platform templates.
- **Creating recipes with invalid stage types / field types:** FluentValidation must check that StageType ∈ {Entry, Active, ClosedWon, ClosedLost}, FieldType matches CustomFieldType enum, etc.
- **Updating a recipe without versioning:** IndustryRecipe.UpdateContent() auto-increments Version — do not skip this.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Recipe CRUD serialization | Custom JSON handling | IndustryRecipe.Create() factory + UpdateContent() + System.Text.Json | Entity factory pattern already established; System.Text.Json handles PascalCase correctly |
| Request validation | Manual if/else checks | FluentValidation nested RequestValidator | Centrally registered validators via ConfigureServices; consistent error reporting across all endpoints |
| Authorization | Custom claim checks | RequireAuthorization() + role filtering via User.IsInRole("SuperAdmin") | ASP.NET Core built-in; JWT claims already extracted; SuperAdmin role seeded in migrations |
| Unique constraint checking | Application-level duplicates | Database unique index + EF Core exception handling | IndustrySlug unique index already exists in IndustryRecipeConfiguration (Phase 6) |
| Recipe content deserialization | Multiple JsonSerializer.Deserialize calls | Single call + cache in service method | Reduces allocations; avoids repeated parsing of same JSONB column |

**Key insight:** The recipe domain is thin — no complex business logic, no state machines, no temporal constraints. Use factories and validators to ensure correctness; EF Core handles persistence.

---

## Common Pitfalls

### Pitfall 1: Anonymous Endpoints Exposing Sensitive Data

**What goes wrong:** Recipe list endpoint is anonymous (correct for signup), but if recipes contained tenant-specific or sensitive metadata, it leaks to unauthenticated users.

**Why it happens:** Recipes are platform templates, not tenant-scoped. But future recipe metadata (e.g., pricing, licensing) might be sensitive.

**How to avoid:** Recipe list and preview endpoints contain only non-sensitive metadata (name, description, icon, counts) and content structure. All sensitive administrative configuration (pricing, license counts, audit trail) lives elsewhere (not in recipe templates for v1.1).

**Warning signs:** If recipes ever include tenant IDs, billing info, or access control metadata — move those fields to a separate admin-only endpoint.

### Pitfall 2: Deactivated Recipes Not Caught During Provisioning

**What goes wrong:** Admin creates a SignupRequest with RecipeId, then deactivates the recipe before provisioning. Provisioning succeeds with a recipe that no longer exists in the catalog, causing confusion.

**Why it happens:** Missing validation guard in ProvisionTenantEndpoint or TenantProvisioningService.

**How to avoid:** Before SeedTenantDataAsync, verify recipe IsActive=true. Return InvalidOperationException if not (locks the SignupRequest until admin reactivates or changes the RecipeId).

**Warning signs:** Tenants provisioned with deactivated recipes; admin unable to revert without manual DB edits.

### Pitfall 3: IndustryType→RecipeId Migration Leaves Orphaned Data

**What goes wrong:** Migration drops IndustryType before existing SignupRequests are migrated to RecipeId, causing data loss or constraint violations.

**Why it happens:** Migration order or schema change timing issues.

**How to avoid:** Migration script: (1) add RecipeId column nullable, (2) populate RecipeId from IndustryType lookup table (or set to Blank GUID if no match), (3) add NOT NULL constraint, (4) drop IndustryType. Two SaveChangesAsync calls if a lookup is needed.

**Warning signs:** Existing SignupRequests with NULL RecipeId; provisioning fails on approved requests.

### Pitfall 4: List Endpoint Performance Degradation

**What goes wrong:** GET /api/recipes deserializes ContentJson for every recipe, becomes slow as catalog grows (50+ recipes).

**Why it happens:** No caching; ContentJson deserialization is per-request.

**How to avoid:** Either (1) store StageCount, FieldCount, RuleCount, RoleCount as computed columns and query only metadata, or (2) cache the list response for 5 minutes. For v1.1, caching is simpler; computed columns can be added in v1.2.

**Warning signs:** List endpoint response time > 100ms with 20+ recipes; database CPU spikes on recipe list requests.

### Pitfall 5: Recipe Content Validation Too Strict or Too Lenient

**What goes wrong:** Validation either blocks valid recipes (overly strict) or allows broken ones that fail during provisioning (too lenient).

**Why it happens:** Validation rules don't match the actual constraints of the provisioning system.

**How to avoid:** Validation rules must match StageType enum values, CustomFieldType enum values, required field checks, and circular-reference checks (e.g., workflow rule referencing stage that doesn't exist in the recipe). Test validation against Automobile and Education recipes from Phase 7.

**Warning signs:** Provisioning fails with deserialization errors; recipes rejected by validator that provision successfully when inserted directly.

---

## Code Examples

Verified patterns from official sources and existing codebase:

### SignupRequest Schema Change (IndustryType → RecipeId)

```csharp
// IronMonkey.Data/Entities/SignupRequest.cs — BEFORE
public string IndustryType { get; private set; } = string.Empty;

// AFTER
public Guid? RecipeId { get; private set; }

// Update factory method
public static SignupRequest.Create(..., Guid? recipeId)
{
    return new SignupRequest(..., recipeId);
}
```

### SignupRequestEndpoint Updated

```csharp
// Source: IronMonkey.ApiService/Authentication/Endpoints/SignupRequestEndpoint.cs
public record Request(
    string CompanyName,
    string AdminEmail,
    string AdminPassword,
    string Phone,
    Guid? RecipeId,  // NEW: optional recipe selection
    string CompanySize,
    string Address,
    string BillingContact);

public class RequestValidator : AbstractValidator<Request>
{
    public RequestValidator()
    {
        // ... existing rules ...
        RuleFor(x => x.RecipeId)
            .NotEmpty()
            .When(x => x.RecipeId.HasValue) // Only validate if provided
            .MustAsync(async (recipeId, ct) =>
            {
                // Validate recipe exists and is active
                using var db = new CentralDbContext(...); // DI required
                return await db.IndustryRecipes
                    .AnyAsync(r => r.Id == recipeId && r.IsActive, ct);
            })
            .WithMessage("Recipe must be active.");
    }
}
```

### Endpoint Registration in Endpoints.cs

```csharp
// Source: IronMonkey.ApiService/Endpoints.cs — existing pattern
extension(IEndpointRouteBuilder app)
{
    private void MapRecipeEndpoints()
    {
        // Public endpoints (anonymous)
        var publicRecipes = app.MapGroup("/api/recipes")
            .WithTags("Recipes")
            .AllowAnonymous();
        publicRecipes.MapEndpoint<RecipeListEndpoint>();
        publicRecipes.MapEndpoint<RecipePreviewEndpoint>();

        // Admin endpoints (SuperAdmin only)
        var adminRecipes = app.MapGroup("/api/recipes")
            .WithTags("Platform Admin")
            .RequireAuthorization();
        adminRecipes.MapEndpoint<CreateRecipeEndpoint>();
        adminRecipes.MapEndpoint<UpdateRecipeEndpoint>();
        adminRecipes.MapEndpoint<DeactivateRecipeEndpoint>();
    }
}

// In MapEndpoints():
endpoints.MapRecipeEndpoints();
```

### Recipe Content FluentValidation

```csharp
// Source: Established FluentValidation pattern
public class RecipeContentValidator : AbstractValidator<RecipeContentModel>
{
    public RecipeContentValidator()
    {
        RuleFor(x => x.PipelineStages)
            .NotEmpty().WithMessage("Recipe must have at least one pipeline stage.")
            .ForEach(s =>
            {
                s.RuleFor(x => x.Name).NotEmpty();
                s.RuleFor(x => x.StageType)
                    .NotEmpty()
                    .Must(st => st is "Entry" or "Active" or "ClosedWon" or "ClosedLost")
                    .WithMessage("Invalid stage type.");
            });

        RuleFor(x => x.CustomFields)
            .ForEach(f =>
            {
                f.RuleFor(x => x.FieldName).NotEmpty();
                f.RuleFor(x => x.FieldType)
                    .Must(ft => ft is "Text" or "Number" or "Date" or "Dropdown" or "Boolean")
                    .WithMessage("Invalid field type.");
            });
    }
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| IndustryType string on SignupRequest | RecipeId GUID reference to IndustryRecipe template | Phase 8 | Enables link to versioned recipe, supports future upgrade tooling |
| Manual recipe seeding (copy-paste) | Atomic provisioning with recipe application | Phase 6-8 | New tenants get consistent, working configuration immediately |
| No recipe selection UI | Recipe list/preview endpoints (API-first) | Phase 8 | Frontend can be built independently; API is testable without Blazor |

**Deprecated/outdated:**
- IndustryType enum or lookup table — replaced with GUID-based recipe references
- Recipe content stored as separate entity rows — centralized to single JSONB column for atomic application

---

## Open Questions

1. **Computed counts vs. deserialization for list response**
   - What we know: List endpoint needs stageCount, fieldCount, ruleCount, roleCount without full content
   - What's unclear: Whether to compute on-demand (deserialize JSONB) or store as denormalized columns
   - Recommendation: For v1.1, deserialize on-demand (simpler migration). Monitor performance; if >100ms, cache for 5 minutes or add computed columns in v1.2

2. **SuperAdmin authorization enforcement**
   - What we know: Recipe admin endpoints require `.RequireAuthorization()` with SuperAdmin role
   - What's unclear: Where is SuperAdmin role assigned? (Seeded in migration or created at first platform admin setup?)
   - Recommendation: SuperAdmin role already seeded in Roles migration (from Phase 1); platform admin setup is out of scope for Phase 8

3. **ProvisionTenantEndpoint: override RecipeId or read from SignupRequest?**
   - What we know: ProvisionTenantEndpoint.ProvisionTenantAsync accepts optional recipeId parameter
   - What's unclear: Should endpoint accept RecipeId in request body to override SignupRequest.RecipeId, or always read from SignupRequest?
   - Recommendation: Always read from SignupRequest. If admin needs to change recipe before provisioning, update the SignupRequest via separate endpoint (simpler, less state confusion)

4. **Migration for SignupRequest schema change**
   - What we know: IndustryType (string) needs to become RecipeId (Guid?)
   - What's unclear: Do existing SignupRequests need data migration? (If so, how to map old industry strings to recipe IDs?)
   - Recommendation: For v1.1 (early in project lifecycle), no existing data: drop IndustryType, add RecipeId nullable. If v1.0 data exists, add two-step migration (copy + convert + drop) in Plan 01.

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 with Testcontainers |
| Config file | Fixtures in Tests/Fixtures/ (PostgreSqlFixture pattern) |
| Quick run command | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeEndpointTests" -x` |
| Full suite command | `dotnet test IronMonkey.Tests -x` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| ONBD-01 | SignupRequest accepts optional RecipeId | Unit | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~SignupRequestEndpointTests" -x` | ❌ Wave 0 |
| ONBD-02 | GET /api/recipes/{id} returns full RecipeContentModel (stages, fields, rules, roles, sampleLeads) | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeEndpointTests.PreviewRecipe" -x` | ❌ Wave 0 |
| ONBD-03 | TenantProvisioningService applies recipe atomically during provisioning | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeProvisioningTests" -x` | ✅ Phase 6 (extends existing tests) |
| ONBD-04 | Tenant can modify recipe-seeded data post-provisioning | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeProvisioningTests" -x` | ✅ Phase 6 (existing provisioning tests verify this) |
| ONBD-05 | GET /api/recipes returns list of active recipes with counts | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeEndpointTests.ListRecipes" -x` | ❌ Wave 0 |
| RADM-01 | POST /api/recipes creates recipe, requires SuperAdmin | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeEndpointTests.CreateRecipe" -x` | ❌ Wave 0 |
| RADM-02 | PUT /api/recipes/{id} updates recipe, auto-increments version | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeEndpointTests.UpdateRecipe" -x` | ❌ Wave 0 |
| RADM-03 | DELETE /api/recipes/{id} deactivates recipe, IsActive=false | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeEndpointTests.DeactivateRecipe" -x` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RecipeEndpointTests" -x` (recipe endpoint tests only)
- **Per wave merge:** `dotnet test IronMonkey.Tests -x` (full suite)
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `IronMonkey.Tests/Integration/RecipeEndpointTests.cs` — covers ONBD-02, ONBD-05, RADM-01, RADM-02, RADM-03
- [ ] `IronMonkey.Tests/Integration/SignupRequestEndpointTests.cs` — covers ONBD-01 (may extend existing signup tests)
- [ ] Test fixtures for SuperAdmin user context (JWT token with SuperAdmin role) — needed for admin endpoint tests
- [ ] RecipeContentValidator tests — verify validation rules reject invalid stage types, field types, etc.

*(Existing test infrastructure: PostgreSqlFixture, TenantDbContextFactory, integration test patterns all in place from Phases 1-7)*

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| PostgreSQL | Recipe CRUD (CentralDbContext) | ✓ | 15-alpine (Docker) | — |
| Docker | Integration tests (Testcontainers) | ✓ | (Docker daemon) | — |
| .NET SDK | Build, test, run | ✓ | 10.0 | — |
| dotnet test command | Validation | ✓ | (built-in) | — |

**Missing dependencies with no fallback:**
- None — all external dependencies (PostgreSQL, Docker, .NET SDK) are standard for existing phases

**Missing dependencies with fallback:**
- None — Phase 8 is API/database only, no new external services required

---

## Sources

### Primary (HIGH confidence)
- Phase 6 CONTEXT.md — IndustryRecipe entity structure, JSONB content, factory methods, unique index on IndustrySlug
- Phase 7 CONTEXT.md — Recipe content application during provisioning, atomic seeding pattern
- CLAUDE.md — Project conventions (C# 14 extensions, entity factory pattern, endpoint IEndpoint interface, nested Request/Response records, nested RequestValidator, file-scoped namespaces)
- IronMonkey.Data/Entities/IndustryRecipe.cs — Entity design, Create()/UpdateContent()/Deactivate() methods, System.Text.Json serialization
- IronMonkey.ApiService/Endpoints.cs — Endpoint registration pattern, route groups, authorization filters, MapEndpoint<T>() extension
- IronMonkey.ApiService/Authentication/Endpoints/SignupRequestEndpoint.cs — Request/Response record pattern, RequestValidator, Handle() signature, AllowAnonymous() usage
- IronMonkey.ApiService/ConfigureServices.cs — Authorization setup, JWT authentication, FluentValidation registration

### Secondary (MEDIUM confidence)
- Phase 6 implementation (06-01-SUMMARY.md, 06-02-SUMMARY.md, 06-03-SUMMARY.md) — confirmed entity patterns and migration structure for recipe model
- IronMonkey.Tests/Integration/RecipeProvisioningTests.cs, BlankRecipeProvisioningTests.cs — integration test patterns for recipe seeding, PostgreSqlFixture usage

### Tertiary (LOW confidence)
- None — all critical findings verified by CONTEXT.md, CLAUDE.md, and existing code

---

## Metadata

**Confidence breakdown:**
- Standard Stack: HIGH — ASP.NET Core, EF Core, FluentValidation, System.Text.Json all in active use in Phases 1-7
- Architecture Patterns: HIGH — Endpoint patterns (IEndpoint, nested records, validators) established across all phases; recipe entity already exists
- API Design: HIGH — All 5 endpoints follow established patterns; authorization model matches existing /admin group conventions
- Pitfalls: HIGH — Deactivated recipe guard, unique constraint, migration data loss, performance issues are standard API concerns; examples from codebase
- Testing: HIGH — xUnit, Testcontainers, PostgreSqlFixture already in use; test patterns established in Phases 1-7

**Research date:** 2026-03-26
**Valid until:** 2026-04-23 (stable domain; recipes and endpoints unlikely to change fundamentally, but implementation details may refine during planning)
