# Phase 6: Recipe Data Model - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-03-26
**Phase:** 06-recipe-data-model
**Areas discussed:** Recipe entity structure, Recipe application strategy, Versioning mechanics, Blank/Custom recipe design
**Mode:** Auto (all decisions auto-selected using recommended defaults)

---

## Recipe Entity Structure

| Option | Description | Selected |
|--------|-------------|----------|
| Single JSONB column | One ContentJson column with typed sections for stages, fields, rules, roles | ✓ |
| Separate JSONB columns | Individual columns per content type (StagesJson, FieldsJson, etc.) | |
| Relational child tables | Normalized recipe content in separate tables with FKs | |

**User's choice:** [auto] Single JSONB column (recommended — aligns with existing JSONB custom field pattern)

| Option | Description | Selected |
|--------|-------------|----------|
| CentralDbContext | Recipes are platform-level, stored in central DB | ✓ |
| TenantDbContext | Recipes stored per-tenant | |

**User's choice:** [auto] CentralDbContext (already decided in PROJECT.md)

| Option | Description | Selected |
|--------|-------------|----------|
| Flat metadata properties | Name, Description, IconIdentifier, IndustrySlug as columns | ✓ |
| Metadata as JSONB | Single MetadataJson column | |

**User's choice:** [auto] Flat metadata properties (recommended — simple, queryable, follows Tenant entity pattern)

---

## Recipe Application Strategy

| Option | Description | Selected |
|--------|-------------|----------|
| Extend SeedTenantDataAsync | Recipe is richer seed data in existing provisioning service | ✓ |
| Separate RecipeApplicationService | New service called after provisioning | |

**User's choice:** [auto] Extend SeedTenantDataAsync (recommended — keeps provisioning in one place)

| Option | Description | Selected |
|--------|-------------|----------|
| Single SaveChangesAsync | All entities in dependency order, one commit | ✓ |
| Multiple SaveChanges with explicit transaction | Separate saves wrapped in BeginTransaction | |

**User's choice:** [auto] Single SaveChangesAsync (recommended — simpler, EF Core handles FK resolution)

---

## Versioning Mechanics

| Option | Description | Selected |
|--------|-------------|----------|
| Integer version field | Simple incrementing int, starting at 1 | ✓ |
| Semantic versioning string | "1.0.0" format string | |
| Timestamp-based | Use UpdatedAt as implicit version | |

**User's choice:** [auto] Integer version field (recommended — per PROJECT.md decision)

| Option | Description | Selected |
|--------|-------------|----------|
| Store recipe ID + version on Tenant | AppliedRecipeId and AppliedRecipeVersion on Tenant entity | ✓ |
| No tracking | Don't record which recipe was applied | |

**User's choice:** [auto] Store on Tenant (recommended — enables future upgrade path)

---

## Blank/Custom Recipe Design

| Option | Description | Selected |
|--------|-------------|----------|
| Same IndustryRecipe entity with IsBlank flag | Uniform handling, no special cases | ✓ |
| Separate BlankRecipe entity | Different entity type | |
| Hardcoded in provisioning | No recipe record, just code | |

**User's choice:** [auto] Same entity with IsBlank flag (recommended — uniform provisioning logic)

| Option | Description | Selected |
|--------|-------------|----------|
| Migration data seed | Seeded on first deployment via EF migration | ✓ |
| Startup seed | Seeded at application startup | |
| Admin creates manually | Blank recipe created via admin API | |

**User's choice:** [auto] Migration data seed (recommended — guaranteed to exist)

---

## Claude's Discretion

- JSONB content structure design (section names, nesting depth, type discrimination)
- EF Core HasConversion implementation for recipe content serialization
- IndustryRecipe entity configuration (index strategy, max lengths)
- Recipe content C# model classes (strongly-typed DTOs for the JSONB sections)
- Exact changes to TenantProvisioningService method signatures
- Migration naming and structure
- Test fixture setup for recipe-based provisioning tests

## Deferred Ideas

- Recipe upgrade/migration for existing tenants — deferred to v1.2
- Admin API for recipe CRUD — Phase 8
- Domain-specific recipe content — Phase 7
- Sample lead seeding — Phase 7
