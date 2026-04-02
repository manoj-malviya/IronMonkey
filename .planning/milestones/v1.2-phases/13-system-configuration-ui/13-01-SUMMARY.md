---
phase: 13-system-configuration-ui
plan: 01
subsystem: api-endpoints
tags: [endpoints, custom-fields, pipeline-stages, workflow-rules, delete, update]
dependency_graph:
  requires: []
  provides:
    - DELETE /api/pipeline-stages/{id}
    - PUT /api/custom-fields/{id}
    - DELETE /api/custom-fields/{id}
    - DELETE /api/workflow-rules/{id}
  affects:
    - IronMonkey.ApiService/Endpoints.cs
    - IronMonkey.Data/Entities/CustomFieldDefinition.cs
tech_stack:
  added: []
  patterns:
    - tenant-scoped minimal API endpoint pattern (ITenantService + ITenantDbContextFactory)
    - soft-delete via Deactivate() for PipelineStage
    - hard-delete via db.Remove() for CustomFieldDefinition and WorkflowRule
key_files:
  created:
    - IronMonkey.ApiService/Features/Leads/PipelineStages/DeletePipelineStageEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/CustomFields/UpdateCustomFieldEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/CustomFields/DeleteCustomFieldEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Workflow/Rules/DeleteWorkflowRuleEndpoint.cs
  modified:
    - IronMonkey.Data/Entities/CustomFieldDefinition.cs
    - IronMonkey.ApiService/Endpoints.cs
decisions:
  - "PipelineStage delete uses Deactivate() (soft-delete via IsActive=false) — entity has explicit Deactivate() method"
  - "CustomFieldDefinition and WorkflowRule use hard-delete (db.Remove()) — neither has IsActive flag or Deactivate()"
  - "CustomFieldDefinition.Update() method added to entity following PipelineStage.Update() factory pattern"
metrics:
  duration: "2 minutes"
  completed_date: "2026-04-01"
  tasks_completed: 2
  files_modified: 6
---

# Phase 13 Plan 01: Missing Backend Endpoints for System Configuration UI Summary

**One-liner:** 4 missing CRUD endpoints (delete pipeline stage, update/delete custom field, delete workflow rule) plus CustomFieldDefinition.Update() method enabling the System Configuration UI.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | DeletePipelineStageEndpoint + UpdateCustomFieldEndpoint + DeleteCustomFieldEndpoint | b1e4de8 | 5 files created/modified |
| 2 | DeleteWorkflowRuleEndpoint + Endpoints.cs registration | a3926e4 | 2 files created/modified |

## What Was Built

### New Endpoints

1. **DELETE /api/pipeline-stages/{id:guid}** — Soft-delete via `stage.Deactivate()` (sets IsActive=false). Returns `Ok({Success, Message})` or `NotFound`.

2. **PUT /api/custom-fields/{id:guid}** — Updates field name, type, required flag, and options. Validates `CustomFieldType` enum and requires options for Dropdown/MultiSelect. Returns `Ok(Response)` or `NotFound` or `BadRequest<string>`.

3. **DELETE /api/custom-fields/{id:guid}** — Hard-delete via `db.CustomFieldDefinitions.Remove(field)`. Returns `Ok({Success, Message})` or `NotFound`.

4. **DELETE /api/workflow-rules/{id:guid}** — Hard-delete via `db.WorkflowRules.Remove(rule)`. Returns `Ok({Success, Message})` or `NotFound`.

### Entity Enhancement

`CustomFieldDefinition.Update(string fieldName, CustomFieldType type, bool isRequired, List<string>? options)` added to the entity — follows the same mutation method pattern as `PipelineStage.Update()`.

### Endpoints.cs Registrations

All 4 new endpoints registered in appropriate groups:
- `UpdateCustomFieldEndpoint.Map(app)` and `DeleteCustomFieldEndpoint.Map(app)` in `MapLeadsEndpoints()`
- `DeletePipelineStageEndpoint.Map(app)` in `MapLeadsEndpoints()` after `UpdatePipelineStageEndpoint.Map(app)`
- `DeleteWorkflowRuleEndpoint.Map(app)` in `MapPipelineEndpoints()` after `UpdateWorkflowRuleEndpoint.Map(app)`

## Verification

```
dotnet build IronMonkey.ApiService  → Build succeeded. 0 Errors.
grep count of 4 new endpoints      → 4
```

Full solution build (`dotnet build IronMonkey.sln`) has 10 pre-existing errors in `IronMonkey.Web` project (unrelated Blazor component issues present before this plan). ApiService builds cleanly with 0 errors.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Removed duplicate MapIngestionEndpoints() method in Endpoints.cs**
- **Found during:** Task 1 (first build attempt)
- **Issue:** Endpoints.cs had `MapIngestionEndpoints()` defined twice (lines 137-157 and 183-202, identical bodies), causing CS0111 compile error: "Type 'Endpoints' already defines a member called 'MapIngestionEndpoints' with the same parameter types"
- **Fix:** Removed the second duplicate declaration (lines 183-202)
- **Files modified:** IronMonkey.ApiService/Endpoints.cs
- **Commit:** b1e4de8 (included in Task 1 commit)

## Known Stubs

None — all endpoints are fully wired with tenant context, real DB operations, and proper response types.

## Self-Check: PASSED

- [x] DeletePipelineStageEndpoint.cs exists and compiled
- [x] UpdateCustomFieldEndpoint.cs exists and compiled
- [x] DeleteCustomFieldEndpoint.cs exists and compiled
- [x] DeleteWorkflowRuleEndpoint.cs exists and compiled
- [x] CustomFieldDefinition.Update() method added
- [x] All 4 endpoints registered in Endpoints.cs (count = 4)
- [x] ApiService build: 0 errors
- [x] Task 1 commit: b1e4de8
- [x] Task 2 commit: a3926e4
