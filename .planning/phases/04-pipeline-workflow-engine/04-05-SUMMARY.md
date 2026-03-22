---
phase: 04-pipeline-workflow-engine
plan: "05"
subsystem: workflow-rules
tags: [workflow, hangfire, background-jobs, notifications, endpoints]
dependency_graph:
  requires:
    - 04-02 (WorkflowRule, Notification entities and EF config)
    - 04-03 (IStateValidationService, MoveLeadEndpoint)
    - 04-04 (ILeadRoutingService, task management)
  provides:
    - IWorkflowRuleEngine with EvaluateAsync for trigger-based rule processing
    - INotificationService for in-app notifications (no email)
    - WorkflowRuleEvaluationJob (Hangfire, trigger-based)
    - TimeElapsedRuleScanJob (Hangfire, recurring hourly)
    - POST /api/workflow-rules
    - GET /api/workflow-rules
    - PUT /api/workflow-rules/{id}
    - POST /api/workflow-rules/{id}/toggle
  affects:
    - ConfigureServices.cs (DI wiring for all Phase 4 services)
    - ConfigureApp.cs (recurring job registration)
    - Endpoints.cs (MapPipelineEndpoints)
tech_stack:
  added: []
  patterns:
    - Hangfire recurring job via RecurringJob.AddOrUpdate (Cron.Hourly)
    - Hangfire triggered job via IWorkflowRuleEngine.EvaluateAsync
    - In-app notification creation via Notification.Create entity factory
    - JSON condition evaluation via JsonDocument for workflow rules
key_files:
  created:
    - IronMonkey.ApiService/Notifications/INotificationService.cs
    - IronMonkey.ApiService/Notifications/NotificationService.cs
    - IronMonkey.ApiService/Features/Leads/Workflow/Rules/IWorkflowRuleEngine.cs
    - IronMonkey.ApiService/Features/Leads/Workflow/Rules/WorkflowRuleEngine.cs
    - IronMonkey.ApiService/Features/Leads/Workflow/Rules/CreateWorkflowRuleEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Workflow/Rules/ListWorkflowRulesEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Workflow/Rules/UpdateWorkflowRuleEndpoint.cs
    - IronMonkey.ApiService/BackgroundJobs/WorkflowRuleEvaluationJob.cs
    - IronMonkey.ApiService/BackgroundJobs/TimeElapsedRuleScanJob.cs
  modified:
    - IronMonkey.ApiService/ConfigureServices.cs
    - IronMonkey.ApiService/ConfigureApp.cs
    - IronMonkey.ApiService/Endpoints.cs
    - IronMonkey.Web/Components/SuperAdmin/CreatePermission.razor (bug fix)
decisions:
  - "WorkflowRuleEngine uses JsonDocument for condition evaluation in Phase 4 v1 instead of full RulesEngine integration (deferred to Phase 5)"
  - "TimeElapsedRuleScanJob iterates all provisioned tenants via ITenantRegistry.GetAllProvisionedTenantsAsync — consistent with OutboxProcessingJob pattern"
  - "NotificationService creates in-app notifications only — no email delivery per D-10"
  - "MapPipelineEndpoints() added to Endpoints.cs as separate method for Phase 4 workflow rule endpoints"
metrics:
  duration_minutes: 8
  completed_date: "2026-03-22"
  tasks_completed: 2
  files_created: 9
  files_modified: 4
requirements_satisfied:
  - PIPE-01
  - PIPE-02
  - PIPE-03
  - PIPE-04
  - PIPE-05
---

# Phase 4 Plan 05: Workflow Rule Engine and Full Phase 4 Wiring Summary

**One-liner:** Workflow rule engine with notify/assign/schedule_task actions, in-app notification service, 2 Hangfire jobs (trigger-based + hourly time-elapsed scan), and all Phase 4 DI registrations and endpoint routing.

## What Was Built

### Task 1: Workflow Rule Engine, Notification Service, Rule Endpoints (commit: 48f8e62)

**INotificationService + NotificationService:**
- Contract: `CreateAsync(TenantDbContext db, Guid tenantId, Guid recipientUserId, string message, Guid? leadId, CancellationToken ct)`
- Implementation: creates `Notification` entity via factory, persists immediately
- In-app only — no email delivery per D-10

**IWorkflowRuleEngine + WorkflowRuleEngine:**
- `EvaluateAsync(tenantId, lead, trigger, db, ct)` — queries active rules matching trigger, evaluates condition, dispatches action
- Condition evaluation via JsonDocument: supports `{"always":true}` pass-through and `{"field":"Source","equals":"Api"}` single-field conditions; full RulesEngine deferred to Phase 5
- Action dispatch: `notify` (creates Notification), `assign` (calls `lead.AssignTo()`), `schedule_task` (creates LeadTask with dueDays offset)
- Per-rule exception handling — continues processing other rules on failure

**Three rule management endpoints:**
- `POST /api/workflow-rules` — create with trigger enum validation
- `GET /api/workflow-rules` — list ordered by CreatedAt
- `PUT /api/workflow-rules/{id}` — update name/trigger/condition/action
- `POST /api/workflow-rules/{id}/toggle` — flip IsActive per D-12 (preserves rule)

### Task 2: Hangfire Jobs and Full DI/Endpoint Wiring (commits: 5ae01ed, d8ba2af)

**WorkflowRuleEvaluationJob** (`[Queue("tenant")]`):
- Parameters: `tenantId`, `leadId`, `trigger` string
- Resolves lead from tenant DB, parses trigger enum, calls `IWorkflowRuleEngine.EvaluateAsync`
- Automatic retry: 3 attempts with delays [30, 120, 300] seconds

**TimeElapsedRuleScanJob** (`[Queue("default")]`):
- Registered as `Cron.Hourly` recurring job "time-elapsed-rule-scan" per D-11
- Iterates all provisioned tenants via `GetAllProvisionedTenantsAsync`
- Skip tenants with no active `WorkflowTrigger.TimeElapsed` rules (early exit)
- Evaluates all non-terminal leads (via `l.Stage.IsTerminal` filter) per tenant

**ConfigureServices.cs additions:**
- `INotificationService` → `NotificationService`
- `IWorkflowRuleEngine` → `WorkflowRuleEngine`
- `WorkflowRuleEvaluationJob` (scoped)
- `TimeElapsedRuleScanJob` (scoped)

**ConfigureApp.cs:** `RecurringJob.AddOrUpdate<TimeElapsedRuleScanJob>(Cron.Hourly)` registered on startup

**Endpoints.cs:** `MapPipelineEndpoints()` added and called from `MapEndpoints()` — registers workflow rule endpoints

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Missing `using IronMonkey.ApiService.Common.Auth` in rule endpoints**
- **Found during:** Task 1 build verification
- **Issue:** `ITenantService` could not be found in endpoint classes — namespace was not included in the plan's code templates
- **Fix:** Added `using IronMonkey.ApiService.Common.Auth;` to all three rule endpoint files
- **Files modified:** CreateWorkflowRuleEndpoint.cs, ListWorkflowRulesEndpoint.cs, UpdateWorkflowRuleEndpoint.cs
- **Commit:** 48f8e62

**2. [Rule 1 - Bug] ITenantRegistry.GetAllTenantsAsync does not exist**
- **Found during:** Task 2 — creating TimeElapsedRuleScanJob
- **Issue:** Plan referenced `GetAllTenantsAsync` returning a list with `.Id` property; actual interface has `GetAllProvisionedTenantsAsync` returning `IReadOnlyList<(Guid TenantId, string ConnectionString)>`
- **Fix:** Used the correct method and destructured the tuple `(tenantId, connectionString)` directly — eliminating the separate `GetConnectionStringAsync` call
- **Files modified:** TimeElapsedRuleScanJob.cs
- **Commit:** 5ae01ed

**3. [Rule 1 - Bug] Pre-existing Blazor naming collision in CreatePermission.razor**
- **Found during:** Task 2 — running `dotnet build IronMonkey.sln`
- **Issue:** `CreatePermission` component had method also named `CreatePermission` — CS0542 naming collision. This blocked the first solution build attempt.
- **Fix:** Renamed method to `HandleCreatePermission`
- **Files modified:** IronMonkey.Web/Components/SuperAdmin/CreatePermission.razor
- **Commit:** 5ae01ed

**4. [Rule 3 - Blocking] Write tool required for ConfigureServices.cs, ConfigureApp.cs, Endpoints.cs**
- **Found during:** Task 2 — after staging and committing
- **Issue:** Edit tool changes to these files were not persisted on disk (system process reverted them after stash operations). Files appeared to be committed but git show HEAD confirmed old content.
- **Fix:** Used Write tool to write complete file content; committed separately as d8ba2af
- **Files modified:** ConfigureServices.cs, ConfigureApp.cs, Endpoints.cs
- **Commits:** d8ba2af

## Deferred Issues

**IronMonkey.Web pre-existing build errors** (out of scope for this plan):
- `ApiClient` static methods `GetAsync`/`PostAsync` not defined in Web project Blazor components
- `JSRuntime` used without `[Inject]` in multiple components
- `dotnet build IronMonkey.sln` does not exit 0 due to these errors
- `dotnet build IronMonkey.ApiService` exits 0 — all ApiService work is complete
- Documented in `.planning/phases/04-pipeline-workflow-engine/deferred-items.md`

## Known Stubs

None — all Phase 4 workflow rule engine components are fully wired with real implementations.

## Self-Check: PASSED

All created files exist:
- IronMonkey.ApiService/Notifications/INotificationService.cs: FOUND
- IronMonkey.ApiService/Notifications/NotificationService.cs: FOUND
- IronMonkey.ApiService/Features/Leads/Workflow/Rules/IWorkflowRuleEngine.cs: FOUND
- IronMonkey.ApiService/Features/Leads/Workflow/Rules/WorkflowRuleEngine.cs: FOUND
- IronMonkey.ApiService/Features/Leads/Workflow/Rules/CreateWorkflowRuleEndpoint.cs: FOUND
- IronMonkey.ApiService/Features/Leads/Workflow/Rules/ListWorkflowRulesEndpoint.cs: FOUND
- IronMonkey.ApiService/Features/Leads/Workflow/Rules/UpdateWorkflowRuleEndpoint.cs: FOUND
- IronMonkey.ApiService/BackgroundJobs/WorkflowRuleEvaluationJob.cs: FOUND
- IronMonkey.ApiService/BackgroundJobs/TimeElapsedRuleScanJob.cs: FOUND

All commits exist:
- 48f8e62: FOUND (Task 1)
- 5ae01ed: FOUND (Task 2 — jobs + web fix)
- d8ba2af: FOUND (Task 2 — DI + endpoint wiring)

Build verification:
- dotnet build IronMonkey.ApiService: 0 errors
