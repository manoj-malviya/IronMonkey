# Phase 6: Recipe Data Model - Research

**Researched:** 2026-03-26
**Domain:** Multi-tenant CRM recipe infrastructure — entity design, JSONB storage, transactional seeding
**Confidence:** HIGH

## Summary

Phase 6 establishes the database and domain model for industry recipe templates — reusable provisioning blueprints stored in the central database and applied atomically to new tenants. The research confirms that the existing codebase patterns (JSONB storage via `HasConversion`, transactional seeding in `TenantProvisioningService`, EF Core 10.0.5 with Npgsql 10.0.1) are ready to support recipe implementation with minimal architectural changes.

The core work is:
1. **IndustryRecipe entity** in CentralDbContext with JSONB content and flat metadata (Name, Description, IconIdentifier, IndustrySlug, Version, IsBlank, IsActive)
2. **Recipe content model** — C# classes wrapping the JSONB payload (PipelineStageDefinition, CustomFieldDefinition, WorkflowRuleDefinition, RoleDefinition)
3. **Recipe application** — extend `TenantProvisioningService.SeedTenantDataAsync()` to load a recipe by ID and apply its content in dependency order (roles → stages → fields → rules)
4. **Blank recipe seeding** — data migration to insert a minimal Blank/Custom recipe (one "New" entry stage, Admin role)
5. **Tenant tracking** — add AppliedRecipeId and AppliedRecipeVersion to Tenant entity for future upgrade tooling

All decisions are locked (D-01 through D-15 in CONTEXT.md). Implementation is straightforward mechanical work following established patterns.

**Primary recommendation:** Design recipe content as strongly-typed C# models (DTOs) with `HasConversion` JSONB serialization matching the WorkflowRule/CustomFieldDefinition pattern. No special frameworks or libraries needed beyond what's already in use.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- **D-01:** IndustryRecipe entity lives in CentralDbContext — recipes are platform-level templates, not per-tenant data
- **D-02:** Single JSONB column (`ContentJson`) stores the full recipe payload with typed sections: pipeline stages, custom field definitions, workflow rules, and default roles. Follows the existing JSONB custom field pattern (HasConversion)
- **D-03:** Flat metadata properties on the entity — Name (string, required), Description (string), IconIdentifier (string, for UI display), IndustrySlug (string, unique identifier like "automobile-dealership")
- **D-04:** IsBlank flag distinguishes the Blank/Custom recipe from domain recipes — enables uniform handling in provisioning (no special-case branching)
- **D-05:** IsActive flag for soft-delete/deactivation — deactivated recipes don't appear in selection list but remain in DB for audit trail
- **D-06:** Extend existing `TenantProvisioningService.SeedTenantDataAsync` — recipe application is richer seed data, not a separate service. The provisioning service accepts a recipe ID and applies it during tenant creation
- **D-07:** Add entities in dependency order within a single `SaveChangesAsync` call: roles first, then pipeline stages, then custom field definitions, then workflow rules. EF Core handles FK resolution within the transaction
- **D-08:** Full transactional atomicity — if any part of recipe application fails, the entire tenant provisioning rolls back. No partial state. Leverages the existing transactional pattern in `TenantProvisioningService`
- **D-09:** Recipe content is **copied** into tenant entities at provisioning time — no FK back to the recipe template. Tenant can freely modify all seeded data. This is already decided in PROJECT.md
- **D-10:** Integer `Version` field on IndustryRecipe, starting at 1, incremented on each recipe update
- **D-11:** Store `AppliedRecipeId` (Guid?) and `AppliedRecipeVersion` (int?) on the Tenant entity — records which recipe and version were used at provisioning. Enables future upgrade tooling (deferred to v1.2)
- **D-12:** No recipe upgrade/migration for existing tenants in v1.1 — recipes are initial provisioning only (per REQUIREMENTS.md Out of Scope)
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

### Deferred Ideas (OUT OF SCOPE)

- Recipe upgrade/migration for existing tenants — explicitly deferred to v1.2 (per REQUIREMENTS.md Out of Scope)
- Admin API for recipe CRUD — Phase 8 scope (RADM-01, RADM-02, RADM-03)
- Recipe preview endpoint — Phase 8 scope (ONBD-02)
- Domain-specific recipe content (Automobile, Educational) — Phase 7 scope (RCNT-01, RCNT-02)
- Sample lead seeding within recipes — Phase 7 scope (RCNT-03)
- Recipe marketplace / community-contributed recipes — future milestone

</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| RCPE-01 | System stores industry recipe templates with pipeline stages, custom fields, workflow rules, and default roles as reusable JSONB definitions in the central database | IndustryRecipe entity in CentralDbContext with ContentJson JSONB column; recipe content model with sections for PipelineStageDefinition, CustomFieldDefinition, WorkflowRuleDefinition, RoleDefinition |
| RCPE-02 | Each recipe has metadata (name, description, icon/identifier) for display during selection | Flat metadata properties: Name, Description, IconIdentifier (for UI display), IndustrySlug (unique identifier) on IndustryRecipe entity |
| RCPE-03 | A "Blank/Custom" recipe exists with minimal defaults (one default stage, Admin role) for tenants without a matching industry | IsBlank flag + data migration seeding Blank recipe with one "New" Entry stage; Admin role already seeded by tenant migration |
| RCPE-04 | Recipes are versioned so changes to a recipe template can be tracked over time | Integer Version field on IndustryRecipe starting at 1; AppliedRecipeVersion stored on Tenant entity to track which version was applied at provisioning |

</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| EF Core | 10.0.5 | ORM and data access | Existing dependency; JSONB support via Npgsql HasConversion; migrations framework |
| Npgsql | 10.0.1 | PostgreSQL driver for EF Core | Version-matched with EF Core 10.0.5; JSONB type support essential for recipe storage |
| System.Text.Json | — | JSONB serialization | Native to .NET; used throughout codebase (CustomFieldDefinition, WorkflowRule, ActivityLog) |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| xUnit | 2.9.3 | Test framework | Existing; all tests written in xUnit; recipe provisioning tests follow TenantProvisioningTests pattern |
| Testcontainers.PostgreSql | 4.3.0 | Real PostgreSQL in tests | Required for integration tests; TenantProvisioningTests uses PostgreSqlFixture |
| Moq | 4.20.72 | Mock framework | Optional; for mocking recipe repository lookups if needed |

**Installation:**
```bash
# Already installed in IronMonkey.Data and IronMonkey.Tests
# No new package references needed for Phase 6
```

**Version verification:** EF Core 10.0.5 (verified 2026-03-26) matches CLAUDE.md pin; Npgsql 10.0.1 (verified in IronMonkey.Data.csproj) is correct match.

## Architecture Patterns

### Recommended Project Structure

```
IronMonkey.Data/
├── Entities/
│   ├── IndustryRecipe.cs                    # NEW: Central DB entity
│   ├── Tenant.cs                             # EXTEND: Add AppliedRecipeId, AppliedRecipeVersion
│   └── [existing entities unchanged]
├── Configurations/
│   ├── IndustryRecipeConfiguration.cs        # NEW: IEntityTypeConfiguration for IndustryRecipe
│   └── [existing configs unchanged]
├── Migrations/Central/
│   ├── 20260326XXXXXX_Phase6_RecipeModel.cs # NEW: Create IndustryRecipe table + Tenant columns
│   └── 20260326XXXXXX_SeedBlankRecipe.cs    # NEW: Insert Blank recipe
└── RecipeContent/                            # NEW: DTO namespace for JSONB payload
    ├── RecipeContentModel.cs                 # Root wrapper with sections
    ├── PipelineStageDefinition.cs
    ├── CustomFieldDefinition.cs
    ├── WorkflowRuleDefinition.cs
    └── RoleDefinition.cs

IronMonkey.ApiService/
├── Authentication/Services/
│   └── TenantProvisioningService.cs          # EXTEND: Accept recipe ID, load, apply
└── [other layers unchanged]

IronMonkey.Tests/Integration/
└── TenantProvisioningTests.cs                # EXTEND: Add recipe-based provisioning tests
```

### Pattern 1: Central Database Platform Entity (IndustryRecipe)

**What:** IndustryRecipe extends `Entity` base (not `BaseTenantEntity` — no TenantId, recipes are platform-wide). Stores both metadata (Name, Description, etc.) and serialized content as JSONB.

**When to use:** Entities that exist at the platform level and are accessed across tenants (tenant templates, system configurations, global data).

**Example:**
```csharp
// Source: CONTEXT.md D-01, D-02, D-03, D-04, D-05
public sealed class IndustryRecipe : Entity
{
    private IndustryRecipe(Guid id, string name, string description, string industrySlug,
        bool isBlank, RecipeContentModel content)
        : base(id)
    {
        Name = name;
        Description = description;
        IndustrySlug = industrySlug;
        IsBlank = isBlank;
        ContentJson = System.Text.Json.JsonSerializer.Serialize(content);
        Version = 1;
        IsActive = true;
    }

    private IndustryRecipe() { }

    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string IndustrySlug { get; private set; } = string.Empty;    // "automobile-dealership", unique
    public string IconIdentifier { get; private set; } = string.Empty;   // UI display identifier
    public bool IsBlank { get; private set; }                            // Distinguishes Blank/Custom
    public bool IsActive { get; private set; } = true;                   // Soft-delete flag
    public int Version { get; private set; } = 1;
    public string ContentJson { get; private set; } = "{}";              // JSONB payload

    public static IndustryRecipe Create(string name, string description, string industrySlug,
        string iconIdentifier, bool isBlank, RecipeContentModel content)
    {
        return new IndustryRecipe(Guid.NewGuid(), name, description, industrySlug, isBlank, content);
    }

    public void UpdateContent(RecipeContentModel content)
    {
        ContentJson = System.Text.Json.JsonSerializer.Serialize(content);
        Version++;  // D-10: auto-increment on update
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
```

### Pattern 2: JSONB HasConversion with Strongly-Typed DTOs

**What:** JSONB columns use `HasConversion<T>` to serialize/deserialize complex objects. Follow the existing pattern from CustomFieldDefinition and WorkflowRule.

**When to use:** Complex nested data that doesn't need row-level querying. Store as JSON, hydrate to typed objects in application.

**Example:**
```csharp
// Source: IronMonkey.Data/Configurations/CustomFieldDefinitionConfiguration.cs pattern
// Source: CONTEXT.md D-02 (follows existing JSONB custom field pattern)

public sealed class RecipeContentModel
{
    public List<PipelineStageDefinition> PipelineStages { get; set; } = [];
    public List<CustomFieldDefinitionDto> CustomFields { get; set; } = [];
    public List<WorkflowRuleDefinition> WorkflowRules { get; set; } = [];
    public List<RoleDefinition> Roles { get; set; } = [];
}

public sealed class PipelineStageDefinition
{
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public string StageType { get; set; } = "Active"; // "Entry", "Active", "ClosedWon", "ClosedLost"
}

// Configuration in IndustryRecipeConfiguration:
builder.Property(r => r.ContentJson)
    .HasConversion(
        v => System.Text.Json.JsonSerializer.Serialize(v,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }),
        v => System.Text.Json.JsonDeserialize<RecipeContentModel>(v,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })
            ?? new RecipeContentModel())
    .HasColumnType("jsonb");
```

### Pattern 3: Transactional Seeding with Dependency Ordering

**What:** Extend `TenantProvisioningService.SeedTenantDataAsync()` to accept a recipe, deserialize its content, and apply entities in dependency order within a single `SaveChangesAsync()` call. EF Core FK resolution happens within the transaction.

**When to use:** Applying templated data during provisioning; any scenario where multiple related entities must be created atomically.

**Example:**
```csharp
// Source: CONTEXT.md D-06, D-07, D-08
// Extends: IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs

private async Task SeedTenantDataAsync(
    TenantDbContext db,
    Tenant tenant,
    SignupRequest signupRequest,
    Guid? recipeId,  // NEW parameter
    CancellationToken cancellationToken)
{
    // Step 1: Load recipe from central DB (if provided)
    IndustryRecipe? recipe = null;
    if (recipeId.HasValue)
    {
        recipe = await _centralDb.IndustryRecipes
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == recipeId, cancellationToken);

        if (recipe == null)
            throw new InvalidOperationException($"Recipe {recipeId} not found.");
    }

    // Step 2: Add roles (D-07: roles first)
    var adminRole = await db.Roles.SingleAsync(r => r.Name == "Admin", cancellationToken);

    if (recipe != null)
    {
        var content = System.Text.Json.JsonSerializer.Deserialize<RecipeContentModel>(recipe.ContentJson);
        foreach (var roleDef in content?.Roles ?? [])
        {
            // Tenant can define custom roles (Phase 8 scope), but for Phase 6, use seeded defaults
            // or extend Role entity to support custom roles
        }
    }

    // Step 3: Add pipeline stages (D-07: stages second)
    if (recipe != null)
    {
        var content = System.Text.Json.JsonSerializer.Deserialize<RecipeContentModel>(recipe.ContentJson);
        foreach (var stageDef in content?.PipelineStages ?? [])
        {
            var stage = PipelineStage.Create(
                tenant.Id,
                stageDef.Name,
                stageDef.Order,
                Enum.Parse<StageType>(stageDef.StageType));
            db.PipelineStages.Add(stage);
        }
    }
    else
    {
        // Blank recipe: one default "New" Entry stage
        var newStage = PipelineStage.Create(tenant.Id, "New", 0, StageType.Entry);
        db.PipelineStages.Add(newStage);
    }

    // Step 4: Add custom field definitions
    if (recipe != null)
    {
        var content = System.Text.Json.JsonSerializer.Deserialize<RecipeContentModel>(recipe.ContentJson);
        foreach (var fieldDef in content?.CustomFields ?? [])
        {
            var field = CustomFieldDefinition.Create(
                tenant.Id,
                fieldDef.FieldName,
                fieldDef.FieldType,
                fieldDef.IsRequired,
                fieldDef.Options);
            db.CustomFieldDefinitions.Add(field);
        }
    }

    // Step 5: Add workflow rules
    if (recipe != null)
    {
        var content = System.Text.Json.JsonSerializer.Deserialize<RecipeContentModel>(recipe.ContentJson);
        foreach (var ruleDef in content?.WorkflowRules ?? [])
        {
            var rule = WorkflowRule.Create(
                tenant.Id,
                ruleDef.Name,
                ruleDef.Trigger,
                ruleDef.ConditionJson,
                ruleDef.ActionJson);
            db.WorkflowRules.Add(rule);
        }
    }

    // Step 6: Seed admin user
    var adminUser = User.Create(
        tenant.Id,
        signupRequest.AdminEmail.Split('@')[0],
        signupRequest.AdminEmail,
        signupRequest.AdminPasswordHash,
        adminRole);
    db.Users.Add(adminUser);

    // Step 7: Single SaveChangesAsync — atomicity per D-08
    await db.SaveChangesAsync(cancellationToken);

    // Step 8: Track applied recipe in central DB (D-11)
    if (recipe != null)
    {
        tenant.SetAppliedRecipe(recipe.Id, recipe.Version);
    }
}
```

### Pattern 4: Blank/Custom Recipe as Regular Row with Flag

**What:** Blank recipe is stored as a normal IndustryRecipe row with `IsBlank = true`. Provisioning logic treats it identically to domain recipes — no special casing, just check the flag.

**When to use:** Distinguishing a default/fallback template from domain-specific ones without branching logic.

**Example:**
```csharp
// Blank recipe seeding in migration:
migrationBuilder.InsertData(
    table: "industry_recipes",
    columns: new[] { "Id", "Name", "Description", "IndustrySlug", "IconIdentifier", "IsBlank", "IsActive", "Version", "ContentJson", "CreatedAt", "UpdatedAt" },
    values: new object[]
    {
        Guid.Parse("00000000-0000-0000-0000-000000000001"), // Fixed ID for deterministic lookup
        "Blank / Custom",
        "Minimal workspace with one default stage and Admin role",
        "blank-custom",
        "blank",
        true,  // IsBlank = true
        true,  // IsActive = true
        1,
        """{"pipelineStages":[{"name":"New","order":0,"stageType":"Entry"}],"customFields":[],"workflowRules":[],"roles":[]}""",
        DateTime.UtcNow,
        DateTime.UtcNow
    });
```

### Anti-Patterns to Avoid

- **Storing recipe entities with FK to original template:** D-09 is explicit — recipe content is copied, not referenced. Tenants own their data independently.
- **Special casing Blank recipe in provisioning logic:** D-04, D-14 prevent this. IsBlank flag means provisioning treats it uniformly.
- **Creating recipes via service layer in provisioning:** D-14, D-15 — Blank recipe is seeded via migration, domain recipes are admin-created (Phase 8). No dynamic recipe creation in Phase 6.
- **Ignoring transactional atomicity:** D-08 is non-negotiable. Any failure in seeding must roll back the entire provisioning. Use a single SaveChangesAsync.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Serializing complex nested objects to/from JSON | Custom JSON serializer or manual string concatenation | System.Text.Json with HasConversion (existing pattern) | Industry standard; built-in to .NET; handles escaping, null safety, type mismatches automatically |
| Managing transactional consistency across multiple entity types | Manual transaction management with rollback logic | EF Core SaveChangesAsync within implicit transaction (existing pattern) | DbContext manages transaction lifecycle; automatic rollback on exception; prevents partial writes |
| Versioning template changes | Custom version tracking table or archive pattern | Integer Version field incremented in entity (D-10) | Simple, auditable, sufficient for tracking which version a tenant provisioned with |
| Determining which recipe to apply during provisioning | Manual lookup logic with fallback branching | Pass recipe ID through provisioning service, fail loudly if not found | Explicit intent; errors surface immediately; prevents silent fallbacks masking misconfiguration |

**Key insight:** The codebase already handles all of these problems elegantly. Recipe implementation is bolting new entities into existing infrastructure, not inventing solutions.

## Runtime State Inventory

This section applies to rename/refactor/migration phases. **Omitted for Phase 6** — greenfield entity addition, no existing state to migrate.

## Common Pitfalls

### Pitfall 1: Creating Recipes without Versioning Infrastructure
**What goes wrong:** Recipes are created without a Version field; later updates have no way to track which version a tenant is running. Recipe schema changes break tenant deployments.

**Why it happens:** Temptation to defer versioning as "future work" (like v1.2 upgrade tooling). But the field itself is trivial to add now.

**How to avoid:** D-10 is locked — implement Version field from day one. Start at 1, increment on every recipe content update. Store AppliedRecipeVersion on Tenant (D-11) so future phases can build upgrade tooling.

**Warning signs:** Code that updates recipe content but doesn't increment Version; provisioning that doesn't record which version was applied; no tests for version tracking.

### Pitfall 2: Partial Seeding on Rollback
**What goes wrong:** Recipe application starts, creates some roles and stages, then fails on workflow rules. Provisioning transaction doesn't roll back. Tenant DB is left in inconsistent state (some seeded, some missing, leading to FK errors on later writes).

**Why it happens:** Underestimating complexity of dependency ordering; assuming "partial success is okay." Or using multiple SaveChangesAsync calls instead of one atomic operation.

**How to avoid:** D-08 is absolute — single SaveChangesAsync for all recipe entities. Let EF Core handle the transaction. Order inserts correctly (D-07: roles → stages → fields → rules). Wrap in integration tests that verify rollback behavior.

**Warning signs:** Multiple SaveChangesAsync calls in SeedTenantDataAsync; success/failure logs without corresponding state checks; tests that don't verify rollback on mid-seeding exception.

### Pitfall 3: Recipes Updating When They Should Never Change
**What goes wrong:** Admin updates a recipe after tenants have provisioned from it. Old tenants still have the old version (they own a copy), but new tenants get a different definition. Confusion over which schema applies to which tenant.

**Why it happens:** Confusion over "recipes as templates" vs. "recipes as configurable definitions." Not realizing D-09 (copy, not reference) is load-bearing.

**How to avoid:** D-09 is explicit and absolute — recipe content is snapshot-copied at provisioning. Updating a recipe only affects future provisions. Document this clearly. Defer recipe upgrade/downgrade tooling to v1.2 (D-12).

**Warning signs:** Code that allows FK back to recipe template; updates to recipe flowing through to provisioned tenants; no clarity on whether tenants own or reference recipe data.

### Pitfall 4: Blank Recipe Not Seeded in Migration
**What goes wrong:** First tenant provisioning with blank recipe fails because the recipe row doesn't exist in the central DB. Or Blank recipe is created dynamically on first provisioning, but runs into concurrency issues if two provisions happen simultaneously.

**Why it happens:** Temptation to lazy-initialize the Blank recipe in service code (simpler than a migration). Or assuming "it's just one row, migrations are overkill."

**How to avoid:** D-15 is locked — Blank recipe is seeded via EF Core migration with InsertData. Fixed Guid ID so it's deterministic. Use a proper migration step so it exists before any provisioning happens.

**Warning signs:** Blank recipe created in code (not in migration); Guid.NewGuid() for Blank recipe (non-deterministic); service code checking `if (blankRecipe == null) create it`.

### Pitfall 5: Forgetting AppliedRecipeId/Version Tracking
**What goes wrong:** Provisioning completes successfully, but Tenant entity has no record of which recipe was applied or what version. Future upgrade tooling (v1.2) has no way to identify which tenants need migration.

**Why it happens:** It's easy to forget to update the central Tenant record after seeding completes. Focus on "did the seeding work" but not "did we record what was seeded."

**How to avoid:** D-11 is explicit — add AppliedRecipeId (Guid?) and AppliedRecipeVersion (int?) to Tenant. Set these in TenantProvisioningService **after** successful SeedTenantDataAsync, before final SaveChangesAsync to central DB. Test that these fields are populated.

**Warning signs:** TenantProvisioningService doesn't record recipe metadata; tests don't verify Tenant.AppliedRecipeId is set; upgrade tooling placeholder code assumes this field exists but it's null.

## Code Examples

Verified patterns from official sources:

### Creating a Central DB Entity (IndustryRecipe)

```csharp
// Source: IronMonkey.Data/Entities/Tenant.cs, SignupRequest.cs (Entity base class pattern)
// Pattern: Entity factory with private constructor, nested configuration

using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public sealed class IndustryRecipe : Entity
{
    private IndustryRecipe(Guid id, string name, string description, string industrySlug,
        string iconIdentifier, bool isBlank, string contentJson)
        : base(id)
    {
        Name = name;
        Description = description;
        IndustrySlug = industrySlug;
        IconIdentifier = iconIdentifier;
        IsBlank = isBlank;
        ContentJson = contentJson;
        Version = 1;
        IsActive = true;
    }

    private IndustryRecipe() { }

    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string IndustrySlug { get; private set; } = string.Empty;
    public string IconIdentifier { get; private set; } = string.Empty;
    public bool IsBlank { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int Version { get; private set; } = 1;
    public string ContentJson { get; private set; } = "{}";

    public static IndustryRecipe Create(string name, string description, string industrySlug,
        string iconIdentifier, bool isBlank, string contentJson)
    {
        return new IndustryRecipe(Guid.NewGuid(), name, description, industrySlug,
            iconIdentifier, isBlank, contentJson);
    }

    public void UpdateContent(string contentJson)
    {
        ContentJson = contentJson;
        Version++;
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }
}
```

### Entity Configuration with JSONB HasConversion

```csharp
// Source: IronMonkey.Data/Configurations/CustomFieldDefinitionConfiguration.cs pattern
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class IndustryRecipeConfiguration : IEntityTypeConfiguration<IndustryRecipe>
{
    public void Configure(EntityTypeBuilder<IndustryRecipe> builder)
    {
        builder.ToTable("industry_recipes");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.Description)
            .HasMaxLength(1000);

        builder.Property(r => r.IndustrySlug)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(r => r.IndustrySlug)
            .IsUnique();

        builder.Property(r => r.IconIdentifier)
            .HasMaxLength(100);

        builder.Property(r => r.IsBlank)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(r => r.Version)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(r => r.ContentJson)
            .IsRequired()
            .HasColumnType("jsonb");

        builder.HasIndex(r => new { r.IsActive, r.IsBlank })
            .HasDatabaseName("IX_IndustryRecipes_IsActive_IsBlank");
    }
}
```

### Extending TenantProvisioningService to Accept Recipe

```csharp
// Source: IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs (extended)
// Key: Accept recipe ID in ProvisionTenantAsync, load from central DB, apply in SeedTenantDataAsync

private async Task SeedTenantDataAsync(
    TenantDbContext db,
    Tenant tenant,
    SignupRequest signupRequest,
    Guid? recipeId,
    CancellationToken cancellationToken)
{
    var adminRole = await db.Roles
        .SingleAsync(r => r.Name == "Admin", cancellationToken);

    IndustryRecipe? recipe = null;
    RecipeContentModel? recipeContent = null;

    if (recipeId.HasValue)
    {
        recipe = await _centralDb.IndustryRecipes
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == recipeId && r.IsActive, cancellationToken)
            ?? throw new InvalidOperationException($"Recipe {recipeId} not found or inactive.");

        recipeContent = System.Text.Json.JsonSerializer.Deserialize<RecipeContentModel>(
            recipe.ContentJson) ?? new RecipeContentModel();
    }

    // Apply stages (or blank default)
    var stages = recipeContent?.PipelineStages ?? [];
    if (stages.Count == 0)
    {
        stages = [new PipelineStageDefinition { Name = "New", Order = 0, StageType = "Entry" }];
    }

    foreach (var stageDef in stages)
    {
        var stage = PipelineStage.Create(
            tenant.Id,
            stageDef.Name,
            stageDef.Order,
            Enum.Parse<StageType>(stageDef.StageType));
        db.PipelineStages.Add(stage);
    }

    // Apply custom fields
    foreach (var fieldDef in recipeContent?.CustomFields ?? [])
    {
        var field = CustomFieldDefinition.Create(
            tenant.Id,
            fieldDef.FieldName,
            fieldDef.FieldType,
            fieldDef.IsRequired,
            fieldDef.Options);
        db.CustomFieldDefinitions.Add(field);
    }

    // Apply workflow rules
    foreach (var ruleDef in recipeContent?.WorkflowRules ?? [])
    {
        var rule = WorkflowRule.Create(
            tenant.Id,
            ruleDef.Name,
            ruleDef.Trigger,
            ruleDef.ConditionJson,
            ruleDef.ActionJson);
        db.WorkflowRules.Add(rule);
    }

    // Seed admin user
    var adminUser = User.Create(
        tenant.Id,
        signupRequest.AdminEmail.Split('@')[0],
        signupRequest.AdminEmail,
        signupRequest.AdminPasswordHash,
        adminRole);
    db.Users.Add(adminUser);

    // Atomic save
    await db.SaveChangesAsync(cancellationToken);
}
```

### Data Seed Migration (Blank Recipe)

```csharp
// Source: CONTEXT.md D-15 (migration seeding Blank recipe)
// File: IronMonkey.Data/Migrations/Central/20260326XXXXXX_SeedBlankRecipe.cs

using Microsoft.EntityFrameworkCore.Migrations;

namespace IronMonkey.Data.Migrations.Central
{
    public partial class SeedBlankRecipe : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var blankRecipeId = new Guid("00000000-0000-0000-0000-000000000001");
            var contentJson = """
                {
                  "pipelineStages": [
                    {
                      "name": "New",
                      "order": 0,
                      "stageType": "Entry"
                    }
                  ],
                  "customFields": [],
                  "workflowRules": [],
                  "roles": []
                }
                """;

            migrationBuilder.InsertData(
                table: "industry_recipes",
                columns: new[] { "Id", "Name", "Description", "IndustrySlug", "IconIdentifier", "IsBlank", "IsActive", "Version", "ContentJson", "CreatedAt", "UpdatedAt", "DeletedAt", "IsDeleted" },
                values: new object[]
                {
                    blankRecipeId,
                    "Blank / Custom",
                    "Minimal workspace with one default stage and Admin role",
                    "blank-custom",
                    "blank",
                    true,
                    true,
                    1,
                    contentJson,
                    DateTime.UtcNow,
                    DateTime.UtcNow,
                    null,
                    false
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "industry_recipes",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"));
        }
    }
}
```

### Recipe Content Model DTOs

```csharp
// Source: New in Phase 6; mirrors existing entity structure (PipelineStage, CustomFieldDefinition, WorkflowRule)
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.RecipeContent;

public sealed class RecipeContentModel
{
    public List<PipelineStageDefinition> PipelineStages { get; set; } = [];
    public List<CustomFieldDefinitionDto> CustomFields { get; set; } = [];
    public List<WorkflowRuleDefinition> WorkflowRules { get; set; } = [];
    public List<RoleDefinition> Roles { get; set; } = [];
}

public sealed class PipelineStageDefinition
{
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public string StageType { get; set; } = "Active";  // Entry, Active, ClosedWon, ClosedLost
}

public sealed class CustomFieldDefinitionDto
{
    public string FieldName { get; set; } = string.Empty;
    public CustomFieldType FieldType { get; set; }
    public bool IsRequired { get; set; }
    public List<string> Options { get; set; } = [];
}

public sealed class WorkflowRuleDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Trigger { get; set; } = "FieldChange";  // FieldChange, StatusChange, TimeElapsed
    public string ConditionJson { get; set; } = "{}";
    public string ActionJson { get; set; } = "{}";
}

public sealed class RoleDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = [];  // Reserved for Phase 8+
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Hard-coded default stages per tenant | Recipe-driven provisioning with Blank/Custom fallback | Phase 6 (this phase) | Tenants can select industry-specific templates; defaults are configurable via migration |
| Recipes as mutable shared templates | Recipes as immutable snapshots copied to tenant DB | Phase 6 (this phase) | Tenants own their data; recipe updates don't affect existing provisioned data; enables audit trail |
| Manual provision testing with custom SQL | Recipe-driven test provisioning via TenantProvisioningService | Phase 6 (this phase) | Automated recipe application testing; scaling to multiple recipes simpler |
| No tracking of applied recipe | AppliedRecipeId/AppliedRecipeVersion on Tenant | Phase 6 (this phase) | Future upgrade tooling (v1.2) has metadata to work with; audit trail complete |

**Deprecated/outdated:**
- Hard-coded default roles: Replaced by recipe-driven provisioning. Default roles still migrated to tenant DB, but recipes can now add custom roles (Phase 8+).
- Single static provisioning path: Now parameterized by recipe ID. Blank recipe as fallback ensures backward compatibility.

## Open Questions

1. **Should recipe content have a `Metadata` wrapper or flat structure?**
   - What we know: CONTEXT.md specifies "single JSONB column" with "typed sections" (D-02). No wrapping required.
   - What's unclear: Exact nesting vs. flat structure. Should it be `{ "pipelineStages": [...], "customFields": [...] }` or something else?
   - Recommendation: Use nested structure per recipe model above (matches CONTEXT.md "sections"). Easier to extend later (e.g., add `sampledLeads` for Phase 7).

2. **Should recipe application be idempotent?**
   - What we know: D-08 requires transactional atomicity (all-or-nothing). Current TenantProvisioningService applies recipes once.
   - What's unclear: Should provisioning allow re-applying a recipe to an already-provisioned tenant? Or fail if recipe is already applied?
   - Recommendation: For Phase 6, provisioning is one-time (applied during tenant creation only). Future phases (v1.2 recipe upgrades) will revisit this.

3. **How should recipe validation work?**
   - What we know: Recipe schema is defined by C# DTOs. JSONB is stored unvalidated.
   - What's unclear: Should recipe creation (Phase 8 RADM-01) validate that stages/fields/rules form a coherent schema? Or allow any valid JSON?
   - Recommendation: Defer to Phase 8. For Phase 6, just store the JSON. Validation happens when a recipe is used (provisioning will fail loudly if content is malformed).

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| PostgreSQL | Central DB + per-tenant databases | ✓ | 15.4 (Aspire container) | — |
| .NET 10.0 | Build and runtime | ✓ | 10.0 (from .csproj) | — |
| Docker | Testcontainers.PostgreSql for tests | ✓ | 24.0+ (WSL2) | Can run tests with external PostgreSQL |
| EF Core CLI | Migrations generation | ✓ | 10.0.5 (global or project tool) | Manual migration creation (not recommended) |

**Missing dependencies with no fallback:**
- None — all requirements are available.

**Missing dependencies with fallback:**
- Docker: Tests can run against external PostgreSQL if Docker unavailable (requires manual setup).

## Validation Architecture

**Workflow:** nyquist_validation enabled (default, per `.planning/config.json`).

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 with Testcontainers.PostgreSql 4.3.0 |
| Config file | None (xUnit uses convention-based discovery) |
| Quick run command | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~*TenantProvisioningTests*" -x` |
| Full suite command | `dotnet test IronMonkey.Tests -x` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| RCPE-01 | IndustryRecipe entity in CentralDbContext stores JSONB payload | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~*IndustryRecipeEntityTests*" -x` | ❌ Wave 0 |
| RCPE-02 | Recipe metadata (Name, Description, IconIdentifier, IndustrySlug) readable by provisioning service | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~*RecipeMetadataTests*" -x` | ❌ Wave 0 |
| RCPE-03 | Blank recipe with one Entry stage exists and is applied when no recipe ID provided | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~*BlankRecipeProvisioningTests*" -x` | ❌ Wave 0 |
| RCPE-04 | Recipe Version field increments on content update; AppliedRecipeVersion tracked on Tenant | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~*RecipeVersioningTests*" -x` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~*TenantProvisioningTests*" -x` (existing provisioning tests + new recipe tests)
- **Per wave merge:** `dotnet test IronMonkey.Tests -x` (full test suite)
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `IronMonkey.Tests/Integration/IndustryRecipeEntityTests.cs` — Create, deserialize, update recipe entities; verify JSONB round-trip
- [ ] `IronMonkey.Tests/Integration/RecipeProvisioningTests.cs` — Extend TenantProvisioningTests with recipe-driven provisioning; test dependency ordering; verify rollback on failure
- [ ] `IronMonkey.Tests/Integration/BlankRecipeProvisioningTests.cs` — Verify Blank recipe exists in migrations; verify applied when no recipe ID provided
- [ ] `IronMonkey.Tests/Integration/RecipeVersioningTests.cs` — Verify Version field increments; verify AppliedRecipeId/AppliedRecipeVersion tracked on Tenant
- [ ] Recipe content DTOs — Add to IronMonkey.Data/RecipeContent namespace (domain models, not test helpers)

## Sources

### Primary (HIGH confidence)
- **CONTEXT.md** (06-recipe-data-model/06-CONTEXT.md) — Decisions D-01 through D-15, canonical references to existing code
- **EF Core 10.0.5 documentation** (verified from IronMonkey.Data.csproj) — JSONB/HasConversion patterns
- **Npgsql 10.0.1 documentation** (verified from NuGet manifest) — PostgreSQL JSONB support
- **IronMonkey.Data/Configurations/CustomFieldDefinitionConfiguration.cs** — Existing JSONB HasConversion pattern, applied directly
- **IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs** — Existing seeding pattern, extending for recipes
- **IronMonkey.Tests/Integration/TenantProvisioningTests.cs** — Test framework and fixture pattern (PostgreSqlFixture, xUnit)
- **IronMonkey.Data/Entities/Tenant.cs, SignupRequest.cs** — Entity factory pattern, private constructor convention

### Secondary (MEDIUM confidence)
- **IronMonkey.Data/Entities/PipelineStage.cs, CustomFieldDefinition.cs, WorkflowRule.cs** — Entity structure, option handling, StageType enum (mirrors recipe content DTOs)
- **IronMonkey.Data/Migrations/Tenant/20260320095026_Initial.cs** — Seed data pattern with InsertData; role/permission seeding

## Metadata

**Confidence breakdown:**
- **Standard Stack:** HIGH — All libraries verified in codebase; versions confirmed; JSONB pattern proven by existing code (CustomFieldDefinition, WorkflowRule, ActivityLog)
- **Architecture:** HIGH — All decisions locked in CONTEXT.md; patterns replicate existing proven infrastructure; no new frameworks
- **Pitfalls:** MEDIUM-HIGH — Common pitfalls based on Phase 1-5 experience and transactional seeding risks; versioning/tracking pitfalls based on typical recipe system failures
- **Environment:** HIGH — All dependencies available in dev environment (Docker, .NET 10, PostgreSQL); no installation needed

**Research date:** 2026-03-26
**Valid until:** 2026-04-26 (30 days; .NET/EF Core stable, no breaking changes expected)

---

**End of Research**
