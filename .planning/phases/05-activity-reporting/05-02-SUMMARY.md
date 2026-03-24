---
phase: 05-activity-reporting
plan: 02
subsystem: database
tags: [ef-core, postgresql, jsonb, migration, entity, tenant-isolation]

# Dependency graph
requires:
  - phase: 04-pipeline-workflow-engine
    provides: LeadTask entity with AssignedToUserId, Lead.AssignedToUserId, Phase4_PipelineWorkflow migration
  - phase: 05-01
    provides: Phase 5 planning context and schema decisions

provides:
  - ActivityLog entity with JSONB old/new values, TenantId global query filter, and factory method
  - Opportunity.Amount decimal field for deal value aggregation
  - Phase5_ActivityReporting EF migration with ActivityLogs table + dashboard indexes
  - Performance indexes on Lead(TenantId+PipelineStageId/Source/AssignedToUserId/CreatedAt)
  - Performance indexes on LeadTask(TenantId+AssignedToUserId/Status)

affects: [05-03, 05-04, 05-05]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "ActivityLog immutable records: no IsDeleted global filter, only TenantId isolation"
    - "JSONB via HasConversion: same pattern as CustomFieldValues for Dictionary<string, object?>"
    - "Composite dashboard indexes: (TenantId, FilterColumn) pattern for efficient tenant-scoped aggregations"

key-files:
  created:
    - IronMonkey.Data/Entities/ActivityLog.cs
    - IronMonkey.Data/Migrations/Tenant/20260324172831_Phase5_ActivityReporting.cs
    - IronMonkey.Data/Migrations/Tenant/20260324172831_Phase5_ActivityReporting.Designer.cs
  modified:
    - IronMonkey.Data/Entities/Opportunity.cs
    - IronMonkey.Data/TenantDbContext.cs
    - IronMonkey.Data/Migrations/Tenant/TenantDbContextModelSnapshot.cs

key-decisions:
  - "ActivityLog global query filter uses TenantId only (no IsDeleted) — activity logs are immutable audit records"
  - "OldValues/NewValues stored as jsonb via HasConversion — consistent with CustomFieldValues pattern from Phase 2"
  - "Opportunity.Amount defaults to 0m and is mutated via SetAmount() — Create() factory signature unchanged"
  - "Worktree rebased onto phase4 before execution — worktree was initially branched from minimal-api, missing Phase 1-4 entities"

patterns-established:
  - "JSONB HasConversion pattern: serialize via System.Text.Json.JsonSerializer for Dictionary<string,object?> columns"
  - "Composite index naming: IX_{Table}_{TenantId}_{Column} for dashboard performance indexes"

requirements-completed: [ACTV-01, REPT-01]

# Metrics
duration: 20min
completed: 2026-03-24
---

# Phase 5 Plan 2: Data Foundation — ActivityLog Entity and EF Migration Summary

**ActivityLog entity with JSONB change payload + Opportunity.Amount field, registered in TenantDbContext with dashboard indexes, generating Phase5_ActivityReporting EF migration**

## Performance

- **Duration:** ~20 min
- **Started:** 2026-03-24T17:25:00Z
- **Completed:** 2026-03-24T17:50:00Z
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments

- Created ActivityLog entity inheriting BaseTenantEntity with LeadId, ActorId, EventType, EntityType, EntityId, OldValues (JSONB), NewValues (JSONB) and static factory method
- Added Opportunity.Amount decimal field (default 0m) with SetAmount() mutator for deal value aggregation per D-10
- Registered ActivityLog in TenantDbContext with global TenantId query filter, JSONB HasConversion for both dictionary columns, and 3 activity log dashboard indexes
- Added 4 Lead performance indexes (PipelineStageId, Source, AssignedToUserId, CreatedAt) and 2 LeadTask indexes (AssignedToUserId, Status) per D-08
- Generated Phase5_ActivityReporting EF migration; verified it applies cleanly (4/4 TenantProvisioning tests pass)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create ActivityLog entity and add Opportunity.Amount** - `8f38abd` (feat)
2. **Task 2: Register ActivityLog in TenantDbContext and generate EF migration** - `cab6075` (feat)

**Plan metadata:** _(docs commit follows)_

## Files Created/Modified

- `IronMonkey.Data/Entities/ActivityLog.cs` - New entity: sealed class with JSONB OldValues/NewValues, Lead/User navigation, and ActivityLog.Create() factory
- `IronMonkey.Data/Entities/Opportunity.cs` - Added Amount decimal field (0m default) and SetAmount() mutator
- `IronMonkey.Data/TenantDbContext.cs` - Added DbSet<ActivityLog>, TenantId query filter, JSONB configuration, and dashboard indexes for ActivityLogs, Leads, and LeadTasks
- `IronMonkey.Data/Migrations/Tenant/20260324172831_Phase5_ActivityReporting.cs` - EF migration: ActivityLogs table, Opportunity.Amount column, 9 new indexes
- `IronMonkey.Data/Migrations/Tenant/20260324172831_Phase5_ActivityReporting.Designer.cs` - Migration designer snapshot
- `IronMonkey.Data/Migrations/Tenant/TenantDbContextModelSnapshot.cs` - Updated model snapshot

## Decisions Made

- ActivityLog global query filter uses TenantId only (no IsDeleted) — activity logs are immutable audit records that should never be soft-deleted
- OldValues/NewValues use HasConversion JSONB serialization (System.Text.Json.JsonSerializer) — consistent with Phase 2 CustomFieldValues pattern; EF Core 10 ToJson doesn't support Dictionary<string,object?> navigation properties
- Opportunity.Amount defaults to 0m and is mutated via SetAmount() — existing Create() factory signature preserved for backward compat
- Worktree rebased onto phase4 before execution: worktree-agent-ae0705d6 was branched from minimal-api commit (ce8771c), missing all Phase 1-4 entity work; rebased to inherit phase4 HEAD (91918d1)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Rebased worktree onto phase4 branch**
- **Found during:** Pre-task discovery
- **Issue:** Worktree was branched from an old minimal-api commit, missing all Phase 1-4 entities (LeadTask, ActivityLog dependencies, Phase4 migration). EF migration would have failed.
- **Fix:** `git rebase phase4` to bring worktree current with all prior phase work
- **Files modified:** All Phase 1-4 files now present
- **Verification:** `ls IronMonkey.Data/Entities/` showed LeadTask.cs and all Phase 4 entities present
- **Committed in:** N/A (git operation, not code change)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Essential prerequisite — without phase4 content, migration generation would fail due to missing prior migration files.

## Issues Encountered

- NuGet restore needed for IronMonkey.ApiService before EF migration generation (obj/project.assets.json missing after rebase — resolved with `dotnet restore`)

## Known Stubs

None - all fields are wired to the database schema. ActivityLog.OldValues and NewValues will be populated by the ActivityChangeInterceptor implemented in plan 05-03.

## Next Phase Readiness

- Schema foundation is complete: ActivityLog table, Opportunity.Amount, and all dashboard indexes exist in migration
- Plan 05-03 (ActivityChangeInterceptor) can now register the EF Core SaveChanges interceptor to populate ActivityLog records automatically
- Plan 05-04 (timeline endpoints) and 05-05 (dashboard endpoints) can rely on ActivityLogs and indexed Lead/LeadTask columns

---
*Phase: 05-activity-reporting*
*Completed: 2026-03-24*
