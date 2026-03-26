---
phase: 07-recipe-content
plan: "01"
subsystem: recipe-content
tags: [recipe, sample-leads, migration, provisioning]
dependency_graph:
  requires: [06-recipe-data-model/06-03]
  provides: [SampleLeadDefinition DTO, domain recipe seeding, lead seeding during provisioning]
  affects: [TenantProvisioningService, RecipeContentModel, CentralDbContext migrations]
tech_stack:
  added: []
  patterns: [stageMap dictionary for stage name resolution, split SaveChangesAsync for FK dependency ordering]
key_files:
  created:
    - IronMonkey.Data/Migrations/Central/20260326120000_SeedDomainRecipes.cs
  modified:
    - IronMonkey.Data/RecipeContent/RecipeContentModel.cs
    - IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs
decisions:
  - "Split SeedTenantDataAsync SaveChangesAsync into two calls: first flushes stages/fields/rules so stageMap can resolve stage IDs, second saves admin user and leads"
  - "SampleLeads with unmatched stage names are silently skipped (continue) to avoid provisioning failures on partial data"
  - "SeedDomainRecipes migration uses string concatenation (not interpolation) to avoid curly brace conflicts with JSON"
metrics:
  duration: "~6 minutes"
  completed_date: "2026-03-26"
  tasks: 3
  files: 3
---

# Phase 07 Plan 01: Recipe Content — Sample Leads + Domain Recipe Seeding Summary

**One-liner:** Added SampleLeadDefinition DTO, split provisioning SaveChangesAsync for FK ordering, and seeded Automobile + Education recipes with full JSONB content including 5 sample leads each.

## What Was Built

### Task 1: SampleLeadDefinition DTO (RecipeContentModel.cs)

Added `SampleLeadDefinition` class with 7 properties and `SampleLeads` property to `RecipeContentModel`:

- `FirstName`, `LastName`, `Email`, `Mobile` (string, empty default)
- `Source` (string, default "WebForm")
- `StageName` (string, matched against `PipelineStageDefinition.Name` at provisioning time)
- `CustomFieldValues` (Dictionary<string, object?>, initialized to [])

### Task 2: Lead Seeding in TenantProvisioningService

Extended `SeedTenantDataAsync` with a lead seeding block that:
1. Checks if recipe has any `SampleLeads` entries (skips if empty — Blank recipe has none)
2. Flushes stages/fields/rules with first `SaveChangesAsync` so stage rows exist in DB
3. Builds `stageMap` dictionary via `ToDictionaryAsync(s => s.Name, s => s.Id)` filtered by `TenantId`
4. Creates `Lead` entities using `Lead.Create()` factory with resolved stage IDs
5. Applies `CustomFieldValues` via `lead.CustomFields.Set(key, value)` loop
6. Second `SaveChangesAsync` saves admin user and all leads atomically

### Task 3: SeedDomainRecipes Migration

New migration `20260326120000_SeedDomainRecipes.cs` inserts:

**Automobile Dealership** (GUID `00000000-0000-0000-0000-000000000002`, slug `automobile`):
- 6 pipeline stages: Inquiry → Test Drive → Negotiation → F&I → Sold → Lost
- 6 custom fields: Vehicle Make (Dropdown), Vehicle Model (Text), Vehicle Year (Number), Budget Range (Dropdown), Has Trade-In (Boolean), Preferred Contact (Dropdown)
- 2 workflow rules: Notify on Negotiation (StatusChange), Flag Stale Inquiry (TimeElapsed 48h)
- 3 roles: Sales Manager, Sales Executive, BDC Agent
- 5 sample leads: 2 in Inquiry, 1 in Test Drive, 1 in Negotiation, 1 in F&I

**Educational Institution** (GUID `00000000-0000-0000-0000-000000000003`, slug `education`):
- 6 pipeline stages: Inquiry → Application → Under Review → Interview → Enrolled → Declined
- 6 custom fields: Program of Interest (Dropdown), Grade/Year Level (Dropdown), Previous School (Text), Guardian Name (Text), Guardian Phone (Text), Scholarship Needed (Boolean)
- 2 workflow rules: Notify on Review (StatusChange), Flag Stale Inquiry (TimeElapsed 168h)
- 3 roles: Admissions Director, Admissions Officer, Academic Counselor
- 5 sample leads: 2 in Inquiry, 1 in Application, 1 in Under Review, 1 in Interview

## Commits

| Task | Commit | Description |
|------|--------|-------------|
| 1 | c9269b4 | feat(07-01): add SampleLeadDefinition DTO to RecipeContentModel |
| 2 | 2f94670 | feat(07-01): extend TenantProvisioningService to seed sample leads from recipe |
| 3 | f40c3d4 | feat(07-01): seed Automobile and Education recipes via EF Core migration |

## Deviations from Plan

### Pre-existing Issue (Out of Scope)

**IronMonkey.Web build errors:** `IronMonkey.Web` has 10 pre-existing build errors in Blazor SuperAdmin components (`CreatePermission.razor`, `CreateRoleAndAssignPermission.razor`) — static method call issues and missing `ApiClient` methods. These existed before Phase 7 and are unrelated to recipe content changes. Core projects (Data, ApiService, Tests) build cleanly with zero errors.

Per scope boundary rule: logged here, not fixed. `IronMonkey.Data` and `IronMonkey.ApiService` both build with 0 errors.

## Known Stubs

None — all data is fully wired. Sample leads contain realistic placeholder data (example.com emails, +1-555-0xxx phones). Custom field values are populated in all sample leads.

## Verification Results

- `dotnet build IronMonkey.Data.csproj` — 0 errors, 0 errors
- `dotnet build IronMonkey.ApiService.csproj` — 0 errors, 0 errors
- Migration ordering: `SeedDomainRecipes` (120000) follows `SeedBlankRecipe` (065340) correctly
- `SeedDomainRecipes.Down()` deletes both rows by fixed GUID

## Self-Check: PASSED

- [x] IronMonkey.Data/RecipeContent/RecipeContentModel.cs — exists and modified
- [x] IronMonkey.ApiService/Authentication/Services/TenantProvisioningService.cs — exists and modified
- [x] IronMonkey.Data/Migrations/Central/20260326120000_SeedDomainRecipes.cs — created
- [x] Commit c9269b4 — verified via git log
- [x] Commit 2f94670 — verified via git log
- [x] Commit f40c3d4 — verified via git log
