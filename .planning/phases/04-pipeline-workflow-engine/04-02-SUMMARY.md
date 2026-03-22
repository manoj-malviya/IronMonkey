---
phase: 04-pipeline-workflow-engine
plan: 02
subsystem: data-layer
tags: [entities, ef-core, migration, pipeline, workflow, tasks, notifications, routing]
dependency_graph:
  requires:
    - 04-01
  provides:
    - LeadTask entity and DB table
    - WorkflowRule entity and DB table
    - StageTransition entity and DB table
    - Notification entity and DB table
    - RoutingConfig entity and DB table
    - PipelineStage.StageType enum and IsTerminal property
    - Lead.AssignedToUserId FK
    - Phase4_PipelineWorkflow EF migration
  affects:
    - 04-03 (state machine uses StageTransition entity)
    - 04-04 (task management uses LeadTask entity)
    - 04-05 (routing uses RoutingConfig entity)
    - 04-06 (workflow engine uses WorkflowRule entity)
tech_stack:
  added: []
  patterns:
    - EF Core IEntityTypeConfiguration<T> with ToTable(), HasKey(), HasConversion<string>(), HasIndex()
    - Global query filters on TenantDbContext for TenantId + IsDeleted
    - BaseTenantEntity static Create() factory pattern
    - Jsonb column type for WorkflowRule ConditionJson/ActionJson and RoutingConfig TerritoryMapJson
key_files:
  created:
    - IronMonkey.Data/Entities/LeadTask.cs
    - IronMonkey.Data/Entities/WorkflowRule.cs
    - IronMonkey.Data/Entities/StageTransition.cs
    - IronMonkey.Data/Entities/Notification.cs
    - IronMonkey.Data/Entities/RoutingConfig.cs
    - IronMonkey.Data/Configurations/LeadTaskConfiguration.cs
    - IronMonkey.Data/Configurations/WorkflowRuleConfiguration.cs
    - IronMonkey.Data/Configurations/StageTransitionConfiguration.cs
    - IronMonkey.Data/Configurations/NotificationConfiguration.cs
    - IronMonkey.Data/Configurations/RoutingConfigConfiguration.cs
    - IronMonkey.Data/Migrations/Tenant/20260322051253_Phase4_PipelineWorkflow.cs
    - IronMonkey.Data/Migrations/Tenant/20260322051253_Phase4_PipelineWorkflow.Designer.cs
  modified:
    - IronMonkey.Data/Entities/PipelineStage.cs
    - IronMonkey.Data/Entities/Lead.cs
    - IronMonkey.Data/Configurations/PipelineStageConfiguration.cs
    - IronMonkey.Data/TenantDbContext.cs
decisions:
  - StageTransitionEntityConfiguration class name used (not StageTransitionConfiguration) to avoid naming conflict with Stateless state machine configuration planned in 04-03
  - RoutingConfig.IsUnique() on TenantId index enforces one routing config per tenant
  - Global query filters added for all 5 new entity types to enforce tenant isolation automatically
metrics:
  duration: 278 seconds
  completed_date: "2026-03-22"
  tasks_completed: 2
  files_modified: 14
---

# Phase 4 Plan 02: Phase 4 Data Layer — Entities, EF Configurations, and Migration

**One-liner:** Five new entities (LeadTask, WorkflowRule, StageTransition, Notification, RoutingConfig) with EF configurations and a single Phase4_PipelineWorkflow migration adding all Phase 4 tables.

## What Was Built

### Task 1: Phase 4 Entities (commit: 19adedf)

Created 5 new entity files, all following the `BaseTenantEntity` pattern with private constructors and static `Create()` factory methods:

- **LeadTask** — Task linked to a lead with `TaskPriority` (Low/Medium/High/Urgent) and `TaskStatus` (Pending/InProgress/Completed/Cancelled) enums, `AssignedToUserId` nullable FK, and `DueDate`.
- **WorkflowRule** — Automation rule with `WorkflowTrigger` (FieldChange/StatusChange/TimeElapsed) enum, jsonb `ConditionJson` and `ActionJson` for flexible rule storage.
- **StageTransition** — Allowed stage transition pair with `FromStageId` and `ToStageId` navigation to `PipelineStage`.
- **Notification** — In-app notification with `RecipientUserId`, `IsRead` flag, and optional `LeadId` reference.
- **RoutingConfig** — Lead routing config with `RoutingStrategy` (RoundRobin/Territory) and `RoutingDimension` (LeadSource/CustomField) enums, `RoundRobinPointer` for round-robin state, `TerritoryMapJson` for territory rules.

Extended existing entities:
- **PipelineStage** — Added `StageType` enum (Entry/Active/ClosedWon/ClosedLost), `IsTerminal` computed property, `SetStageType()` method, and updated `Create()` factory to accept `stageType` parameter.
- **Lead** — Added `AssignedToUserId` nullable FK, `AssignTo()` method, and `MoveToPipelineStage()` method.

### Task 2: EF Configurations, DbSets, and Migration (commit: 4979eab)

Created 5 new EF configurations:
- `LeadTaskConfiguration` — `lead_tasks` table, `Priority`/`Status` stored as strings, cascade FK to leads, indexes on `(TenantId, AssignedToUserId)` and `(TenantId, LeadId)`.
- `WorkflowRuleConfiguration` — `workflow_rules` table, `ConditionJson`/`ActionJson` as jsonb, index on `(TenantId, IsActive)`.
- `StageTransitionEntityConfiguration` — `stage_transitions` table, Restrict delete on both stage FKs, unique index on `(TenantId, FromStageId, ToStageId)`.
- `NotificationConfiguration` — `notifications` table, composite index on `(TenantId, RecipientUserId, IsRead)`.
- `RoutingConfigConfiguration` — `routing_configs` table, `TerritoryMapJson` as jsonb, unique index on `TenantId` (one config per tenant).

Updated:
- `PipelineStageConfiguration` — Added `StageType` property mapped as string.
- `TenantDbContext` — Added 5 new `DbSet<>` properties and 5 new global query filters enforcing tenant isolation.

Generated migration `20260322051253_Phase4_PipelineWorkflow` that adds all Phase 4 tables.

## Verification Results

```
ls IronMonkey.Data/Migrations/Tenant/ | grep Phase4
# → 20260322051253_Phase4_PipelineWorkflow.cs (exists)

grep "DbSet<LeadTask>" IronMonkey.Data/TenantDbContext.cs
# → public DbSet<LeadTask> LeadTasks => Set<LeadTask>(); (exists)

grep "IsTerminal" IronMonkey.Data/Entities/PipelineStage.cs
# → public bool IsTerminal => StageType is StageType.ClosedWon or StageType.ClosedLost; (exists)

dotnet build IronMonkey.Data → Build succeeded, 0 errors
dotnet build IronMonkey.ApiService → Build succeeded, 0 errors
dotnet build IronMonkey.Tests → Build succeeded, 0 errors
```

## Deviations from Plan

### Auto-noted Out-of-Scope Issues

**Pre-existing IronMonkey.Web build failure** — `IronMonkey.Web/Components/SuperAdmin/CreatePermission.razor(15,24): error CS0542: member names cannot be the same as their enclosing type` exists before this plan's changes. Verified by stashing all changes and confirming the error persists. Logged to deferred-items.

**git stash interference** — During a pre-existing error check, `git stash` was used which accidentally reverted TenantDbContext.cs and PipelineStageConfiguration.cs. The migration had already been generated correctly. Both files were re-edited to restore the changes. The migration file itself was unaffected.

### Naming Deviation

**StageTransitionEntityConfiguration** — Plan note specified to name the EF configuration class `StageTransitionEntityConfiguration` (not `StageTransitionConfiguration`) to avoid naming conflict with the Stateless service planned in 04-03. This was implemented as specified — file is named `StageTransitionConfiguration.cs` but class is `StageTransitionEntityConfiguration`.

## Known Stubs

None — all entities are fully wired with properties, factory methods, EF configurations, DbSets, query filters, and a real migration.

## Decisions Made

1. `StageTransitionEntityConfiguration` class name used to avoid collision with planned Stateless service configuration in 04-03.
2. `RoutingConfig.IsUnique()` on `TenantId` index enforces exactly one routing config per tenant.
3. All 5 new entity types get global query filters in `TenantDbContext.OnModelCreating` for automatic tenant isolation enforcement — consistent with existing entity pattern.

## Self-Check: PASSED

- `/home/manoj/projects/sandbox/IronMonkey/IronMonkey.Data/Entities/LeadTask.cs` — FOUND
- `/home/manoj/projects/sandbox/IronMonkey/IronMonkey.Data/Entities/WorkflowRule.cs` — FOUND
- `/home/manoj/projects/sandbox/IronMonkey/IronMonkey.Data/Entities/StageTransition.cs` — FOUND
- `/home/manoj/projects/sandbox/IronMonkey/IronMonkey.Data/Entities/Notification.cs` — FOUND
- `/home/manoj/projects/sandbox/IronMonkey/IronMonkey.Data/Entities/RoutingConfig.cs` — FOUND
- `/home/manoj/projects/sandbox/IronMonkey/IronMonkey.Data/Migrations/Tenant/20260322051253_Phase4_PipelineWorkflow.cs` — FOUND
- Commit 19adedf — Task 1 entities
- Commit 4979eab — Task 2 configurations and migration
