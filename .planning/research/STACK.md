# Technology Stack: Industry Recipe Templates & Tenant Onboarding

**Project:** IronMonkey v1.1 (Industry Recipes)
**Researched:** 2026-03-24
**Confidence:** HIGH (existing validated stack; minimal new additions)

## Executive Summary

Industry recipe templates for v1.1 require **NO new external dependencies**. The existing .NET 10.0 + EF Core 10.0.5 stack already provides:

- **Entity Framework Core JSONB support** (Npgsql 10.0.1) for storing recipe schema as structured JSON
- **JSON serialization** (Newtonsoft.Json 13.0.3, System.Text.Json built-in) for recipe data modeling
- **Database-per-tenant isolation pattern** already proven in v1.0 for recipe application during provisioning

Recipe templates will be stored as entities in the **CentralDbContext** and applied during tenant provisioning by extending the existing `TenantProvisioningService`. The recipe data model uses the same JSON serialization pattern already proven for custom fields (JSONB with EF Core value converters).

## Recommended Stack

### Core Framework (No Changes)
| Technology | Version | Purpose | Why This Version |
|------------|---------|---------|-----------------|
| .NET | 10.0 | Language runtime | Current LTS; proven in v1.0 |
| .NET Aspire | 13.1.0 | Orchestration & service discovery | Working reliably; no recipe-specific needs |
| Entity Framework Core | 10.0.5 | ORM + JSONB support | Exact match with Npgsql 10.0.1 required |
| Npgsql EF Core PostgreSQL | 10.0.1 | PostgreSQL driver + JSONB type mapping | Critical for JSONB custom fields; matches EF Core 10.0.5 |
| PostgreSQL | 15-alpine (container) | Multi-tenant database | Proven in v1.0 tests; JSONB native support essential |

### JSON Serialization (No New Dependencies)
| Library | Version | Purpose | Why Not Adding More |
|---------|---------|---------|---------------------|
| Newtonsoft.Json | 13.0.3 | JSON serialization (existing) | Already in IronMonkey.Data; sufficient for recipe schema serialization |
| System.Text.Json | Built-in (.NET 10.0) | High-performance JSON APIs | Preferred for new code in .NET 10.0; used alongside Newtonsoft for compatibility |

**Why not add JSON Schema libraries?** v1.0 already serializes JSONB without validation libraries. Recipe schema will follow the same pattern: store structured JSON, validate via C# models + FluentValidation at the API layer, not via JSON schema tools.

### Background Processing (No Changes)
| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| Hangfire | 1.8.17 | Background job processing | Recipe application could be async; existing Hangfire + PostgreSQL storage ready |
| Hangfire.PostgreSql | 1.21.1 | PostgreSQL storage backend | No recipe-specific changes needed |

**Note:** Recipe application during tenant provisioning is synchronous (waits for completion). Hangfire integration optional if future async recipe seeding desired.

### Validation (No Changes)
| Library | Version | Purpose | Why |
|---------|---------|---------|-----|
| FluentValidation | 12.1.1 | Request/response validation | Already integrated; recipe selection DTOs will use standard validators |
| FluentValidation.DependencyInjection | 12.1.1 | DI integration | Existing; no changes needed |

## NEW Entities (NO Library Changes)

The only code additions are entity classes:

### IndustryRecipe (Central DB Entity)

**Storage:** `CentralDbContext` (shared across all tenants)

```csharp
public sealed class IndustryRecipe
{
    public Guid Id { get; private set; }
    public string Industry { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    // Serialized recipe schema (JSONB in PostgreSQL)
    public string PipelineStagesJson { get; private set; } = "{}";
    public string CustomFieldsJson { get; private set; } = "{}";
    public string WorkflowRulesJson { get; private set; } = "{}";
    public string RolesJson { get; private set; } = "{}";

    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
}
```

**Purpose:** Stores reusable recipe templates in central DB.

### RecipeSelection (Signup Extension)

Extend existing `SignupRequest` entity with recipe tracking fields.

## NO Deprecations or Removals

All existing v1.0 dependencies remain:
- BCrypt.Net-Next (4.0.3) — auth unchanged
- FuzzySharp (2.0.0) — duplicate detection unchanged
- CsvHelper (33.1.0) — CSV import unchanged
- Serilog (10.0.0) — logging unchanged
- Stateless (5.20.1) — state machine unchanged
- RulesEngine (6.0.0) — workflow engine unchanged

## Integration Points with Existing Code

### 1. TenantProvisioningService Extension

Extend `ProvisionTenantAsync()` to accept optional `industryRecipeId` parameter and apply recipe during seed phase.

### 2. Recipe Application Service (New)

Create new `IRecipeApplicationService` to deserialize JSONB and create tenant entities (PipelineStage, CustomFieldDefinition, WorkflowRule).

### 3. New REST Endpoints

```
GET /api/recipes — List available recipes for signup UI
GET /api/recipes/{id} — Get recipe details
POST /api/recipes — Admin: create/update recipes (future)
```

All use standard IEndpoint pattern + FluentValidation validators. No new libraries.

### 4. Database Migrations

**Central DB:** New `IndustryRecipes` table + columns in `SignupRequests` for recipe selection.

**Tenant DB:** No changes — recipes applied via existing entity types.

## What NOT to Add

| Library/Pattern | Why NOT |
|-----------------|---------|
| JSON Schema validators | v1.0 validates JSONB via C# models + FluentValidation. Same approach. |
| Recipe versioning libs | Simple timestamps + immutable snapshots. No specialized lib needed. |
| Template engines (Liquid, Scriban) | Recipes are static snapshots. Deserialization + EF entity creation sufficient. |
| GraphQL | REST endpoints cover signup UI needs. |

## Dependency Version Alignment

**CRITICAL:** Packages must align with .NET 10.0 and EF Core 10.0.5:

| Package | Version | Notes |
|---------|---------|-------|
| Microsoft.EntityFrameworkCore | 10.0.5 | Must match Npgsql |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.1 | MUST match EF Core major.minor |
| Microsoft.AspNetCore.* | 10.0.5 | Must match .NET SDK |

## Installation Commands (Reference Only)

No new packages to add. All dependencies already in place from v1.0.

## Architecture Decision: Recipe Storage Pattern

### Chosen: JSONB + Deserialization

Store recipe as JSON columns, deserialize to C# DTOs, create entities in tenant DB.

**Pros:**
- Zero new dependencies
- Reuses EF Core JSONB support proven in v1.0 (custom fields)
- Recipes are immutable snapshots (no sync risk)
- Simple to understand and debug

**Cons:**
- No schema validation at storage layer (validation at API input)
- Recipe evolution requires new versions (no in-place updates)

**Why chosen:** Minimal complexity, proven pattern, no library churn.

## Confidence Assessment

| Area | Level | Notes |
|------|-------|-------|
| Existing stack sufficiency | HIGH | v1.0 proven all patterns needed |
| JSONB recipe storage | HIGH | Same technique used for custom fields |
| Service extension pattern | HIGH | TenantProvisioningService extensible, no breaking changes |
| Version alignment | HIGH | EF Core 10.0.5 + Npgsql 10.0.1 verified compatible |
| Endpoint pattern | HIGH | All v1.0 endpoints follow IEndpoint + FluentValidation pattern |

## Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| JSONB deserialization fails | Deserialize in unit tests before applying; catch with logging |
| Provisioning takes too long | Recipe seeding is bulk inserts (~100ms). Monitor in integration tests. |
| Recipe schema evolves | Immutable versions: new IndustryRecipe ID, old recipes remain. No breaking changes. |
| Recipe applied twice | TenantProvisioningService already idempotent via database uniqueness checks. |

## Sources

- **Context7:** EF Core 10.0.5 / Npgsql 10.0.1 compatibility verified
- **PostgreSQL:** JSONB native support
- **IronMonkey v1.0:** JSONB pattern in CustomFieldDefinition, WorkflowRule
- **EF Core docs:** Value Converters for JSONB serialization (no new libs needed)
