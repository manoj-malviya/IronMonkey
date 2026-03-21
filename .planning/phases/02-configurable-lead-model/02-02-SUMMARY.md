---
phase: 02-configurable-lead-model
plan: "02"
subsystem: database
tags: [efcore, postgresql, jsonb, migrations, entities, multi-tenant]

# Dependency graph
requires:
  - phase: 02-01
    provides: Wave 0 test stubs for LEAD-01, LEAD-02, LEAD-03 requirements
  - phase: 01-multi-tenancy-foundation
    provides: BaseTenantEntity, TenantDbContext, EF Core configuration patterns
provides:
  - PipelineStage entity with tenant-scoped ordering and factory Create()
  - CustomFieldDefinition entity with CustomFieldType enum and factory Create()
  - CustomFieldValues value object stored as JSONB in leads table
  - LeadMerge audit entity with source/target snapshots and factory Create()
  - Updated Lead entity with LeadSource enum, PipelineStageId FK, CustomFieldValues navigation
  - 4 new EF configurations (PipelineStage, CustomFieldDefinition, LeadMerge, updated Lead)
  - Phase2_LeadModel tenant migration creating pipeline_stages, custom_field_definitions, lead_merges tables
  - FuzzySharp 2.0.0 in IronMonkey.Data for duplicate detection
  - TenantDbContext extended with 3 new DbSets and query filters
affects:
  - 02-03 (lead CRUD endpoints depend on LeadSource enum and PipelineStageId)
  - 02-04 (duplicate detection endpoints use LeadMerge and FuzzySharp)
  - 02-05 (custom fields endpoints use CustomFieldDefinition and CustomFieldValues)
  - 02-06 (integration test implementations use all entities)

# Tech tracking
tech-stack:
  added:
    - FuzzySharp 2.0.0 (fuzzy string matching for duplicate detection)
  patterns:
    - EF Core value converter for JSONB serialization (CustomFieldValues as Dictionary<string,object?>)
    - LeadSource enum stored as string via HasConversion<string>()
    - Tenant query filters for PipelineStage (IsDeleted), CustomFieldDefinition (IsDeleted), LeadMerge (TenantId only)
    - Unique composite index on TenantId+Order for PipelineStage ordering

key-files:
  created:
    - IronMonkey.Data/Entities/PipelineStage.cs
    - IronMonkey.Data/Entities/CustomFieldDefinition.cs
    - IronMonkey.Data/Entities/CustomFieldValues.cs
    - IronMonkey.Data/Entities/LeadMerge.cs
    - IronMonkey.Data/Configurations/CustomFieldDefinitionConfiguration.cs
    - IronMonkey.Data/Configurations/PipelineStageConfiguration.cs
    - IronMonkey.Data/Configurations/LeadMergeConfiguration.cs
    - IronMonkey.Data/Migrations/Tenant/20260321072845_Phase2_LeadModel.cs
  modified:
    - IronMonkey.Data/Entities/Lead.cs
    - IronMonkey.Data/Configurations/LeadConfiguration.cs
    - IronMonkey.Data/TenantDbContext.cs
    - IronMonkey.Data/IronMonkey.Data.csproj

key-decisions:
  - "CustomFieldValues stored via HasConversion JSONB value converter (not OwnsOne.ToJson) — EF Core 10 ToJson does not support Dictionary<string,object?> navigation properties; value converter achieves identical jsonb column storage"
  - "FuzzySharp 2.0.0 used instead of 1.11.0 — 1.11.0 not available on NuGet; 2.0.0 resolved automatically"
  - "LeadSource enum stored as string (not int) for human-readable DB values and migration safety"
  - "LeadMerge query filter on TenantId only (no IsDeleted) — merge records are immutable audit logs"

patterns-established:
  - "Value converter pattern for JSONB: HasConversion(serialize, deserialize) with HasColumnType(jsonb)"
  - "Enum-as-string: HasConversion<string>() with HasDefaultValue for EF migrations"
  - "Audit entity pattern (LeadMerge): immutable, no IsDeleted filter, TenantId-only query filter"

requirements-completed: [LEAD-01, LEAD-02, LEAD-03, LEAD-05]

# Metrics
duration: 16min
completed: 2026-03-21
---

# Phase 02 Plan 02: Data Layer Summary

**EF Core data layer with LeadSource enum, PipelineStage FK, JSONB custom fields, and Phase2_LeadModel migration across 5 new entity files and 4 EF configuration files**

## Performance

- **Duration:** 16 min
- **Started:** 2026-03-21T07:23:36Z
- **Completed:** 2026-03-21T07:39:00Z
- **Tasks:** 2
- **Files modified:** 12

## Accomplishments

- 4 new entity files: PipelineStage, CustomFieldDefinition, CustomFieldValues, LeadMerge with factory `Create()` methods
- Updated Lead entity: LeadSource enum replaces string, PipelineStageId FK added, CustomFieldValues navigation added
- Phase2_LeadModel EF migration generated and verified in migrations list; creates 3 new tables and updates leads table
- TenantDbContext extended with 3 new DbSets (PipelineStages, CustomFieldDefinitions, LeadMerges) with global query filters

## Task Commits

Each task was committed atomically:

1. **Task 1: Create new entities** - `c8b19f1` (feat)
2. **Task 2: Update Lead, add EF configurations, migration** - `1e889f7` (feat)

**Plan metadata:** [to be added after final commit]

## Files Created/Modified

- `IronMonkey.Data/Entities/PipelineStage.cs` - Tenant-scoped pipeline stage with Name, Order, IsActive and factory
- `IronMonkey.Data/Entities/CustomFieldDefinition.cs` - Tenant field schema with CustomFieldType enum (Text/Number/Date/Dropdown/MultiSelect/Currency/Boolean)
- `IronMonkey.Data/Entities/CustomFieldValues.cs` - JSON value object: Dictionary<string,object?> with Set/Get methods
- `IronMonkey.Data/Entities/LeadMerge.cs` - Immutable merge audit record with source/target JSON snapshots
- `IronMonkey.Data/Entities/Lead.cs` - Updated: LeadSource enum, PipelineStageId FK, CustomFields navigation, updated Create/UpdateLeadInfo signatures
- `IronMonkey.Data/Configurations/LeadConfiguration.cs` - Updated: JSONB value converter for CustomFields, Restrict FK for Stage, string conversion for Source enum
- `IronMonkey.Data/Configurations/CustomFieldDefinitionConfiguration.cs` - FieldType as string, Options as JSON text
- `IronMonkey.Data/Configurations/PipelineStageConfiguration.cs` - Unique TenantId+Order composite index
- `IronMonkey.Data/Configurations/LeadMergeConfiguration.cs` - Restrict FKs to leads, text columns for snapshots
- `IronMonkey.Data/TenantDbContext.cs` - 3 new DbSets, 3 new HasQueryFilter expressions
- `IronMonkey.Data/IronMonkey.Data.csproj` - FuzzySharp 2.0.0 added
- `IronMonkey.Data/Migrations/Tenant/20260321072845_Phase2_LeadModel.cs` - EF migration for all Phase 2 schema changes

## Decisions Made

1. **JSONB storage via HasConversion, not OwnsOne.ToJson** — EF Core 10's `OwnsOne().ToJson()` does not support `Dictionary<string, object?>` as a navigation property (throws at design time). Value converter achieves the same `jsonb` column type with correct serialization.

2. **FuzzySharp 2.0.0** — NuGet resolved 2.0.0 because 1.11.0 does not exist. Updated csproj to reference 2.0.0 explicitly.

3. **LeadSource as string enum** — `HasConversion<string>()` stores enum names ("Manual"/"Import"/"Api"/"WebForm") in the database rather than integers, making DB records human-readable and migration-safe.

4. **LeadMerge query filter: TenantId only** — Merge audit records are immutable; applying IsDeleted filter would be misleading. TenantId filter is sufficient for isolation.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Replaced OwnsOne().ToJson() with HasConversion JSONB value converter for CustomFieldValues**
- **Found during:** Task 2 (migration generation)
- **Issue:** `dotnet ef migrations add` threw `The navigation 'CustomFieldValues.Values' must be configured in 'OnModelCreating' with an explicit name for the target shared-type entity type`. EF Core 10's `ToJson()` does not support `Dictionary<string, object?>` navigations.
- **Fix:** Replaced `builder.OwnsOne(l => l.CustomFields, cf => { cf.ToJson(...); })` with `builder.Property(l => l.CustomFields).HasColumnType("jsonb").HasConversion(...)` using `System.Text.Json.JsonSerializer`. The JSONB column storage and behavior are identical.
- **Files modified:** `IronMonkey.Data/Configurations/LeadConfiguration.cs`
- **Verification:** `dotnet ef migrations add Phase2_LeadModel` succeeded; migration file shows `custom_field_values` column with type `jsonb`
- **Committed in:** `1e889f7` (Task 2 commit)

**2. [Rule 3 - Blocking] FuzzySharp version corrected from 1.11.0 to 2.0.0**
- **Found during:** Task 1 (package add)
- **Issue:** `dotnet add package FuzzySharp --version 1.11.0` resolved to 2.0.0 with NU1603 warning — 1.11.0 does not exist on NuGet
- **Fix:** Updated csproj PackageReference to `Version="2.0.0"` to match resolved version
- **Files modified:** `IronMonkey.Data/IronMonkey.Data.csproj`
- **Verification:** `dotnet build IronMonkey.Data` succeeds with 0 errors
- **Committed in:** `c8b19f1` (Task 1 commit)

---

**Total deviations:** 2 auto-fixed (1 bug fix, 1 blocking resolution)
**Impact on plan:** Both fixes necessary for correctness. JSONB storage semantics identical to planned OwnsOne approach. No scope creep.

## Issues Encountered

- MSBuild parallel node contention: VS Code C# Dev Kit holds long-running MSBuild nodes that lock `obj/` cache files; builds with `-q` incorrectly reported errors. Resolved by running builds without `-q` flag and using `MSBUILDDISABLENODEREUSE=1`. The IronMonkey.Web project has a pre-existing error (`CS0542` in CreatePermission.razor) unrelated to this plan's changes.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All Phase 2 data entities and EF configurations are in place
- Phase2_LeadModel migration ready to apply to tenant databases during provisioning
- Plans 02-03 (lead CRUD endpoints) and 02-04 (duplicate detection) can now build on complete entity contracts
- FuzzySharp 2.0.0 available for duplicate detection implementation in Plan 02-04

## Self-Check: PASSED

All 8 created files verified on disk. Both task commits (c8b19f1, 1e889f7) verified in git log.

---
*Phase: 02-configurable-lead-model*
*Completed: 2026-03-21*
