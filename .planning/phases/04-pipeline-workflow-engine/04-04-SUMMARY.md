---
phase: 04-pipeline-workflow-engine
plan: "04"
subsystem: api
tags: [dotnet, csharp, minimal-api, lead-tasks, routing, round-robin, territory]

requires:
  - phase: 04-02
    provides: LeadTask and RoutingConfig entities, TenantDbContext DbSets

provides:
  - POST /api/leads/{leadId}/tasks - create tasks linked to leads with priority/assignee
  - GET /api/tasks - list tasks filtered by assignee or lead
  - PUT /api/tasks/{taskId} - update task status/priority/assignee
  - ILeadRoutingService interface and LeadRoutingService implementation
  - POST /api/routing-config - upsert routing configuration (one per tenant, D-15)
  - GET /api/routing-config - retrieve current routing configuration

affects:
  - 04-05
  - 04-06

tech-stack:
  added: []
  patterns:
    - "Round-robin pointer pattern: RoundRobinPointer advanced atomically in same SaveChangesAsync as lead assignment"
    - "D-16 null return pattern: territory routing returns null if no dimension value matches map"
    - "Upsert pattern: ConfigureRoutingEndpoint uses FirstOrDefaultAsync then Add-or-Update for idempotent config"

key-files:
  created:
    - IronMonkey.ApiService/Features/Leads/Pipeline/Tasks/CreateTaskEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Pipeline/Tasks/ListTasksEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Pipeline/Tasks/UpdateTaskEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Pipeline/Routing/ILeadRoutingService.cs
    - IronMonkey.ApiService/Features/Leads/Pipeline/Routing/LeadRoutingService.cs
    - IronMonkey.ApiService/Features/Leads/Pipeline/Routing/ConfigureRoutingEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Pipeline/Routing/GetRoutingConfigEndpoint.cs
  modified:
    - IronMonkey.ApiService/Endpoints.cs
    - IronMonkey.ApiService/ConfigureServices.cs

key-decisions:
  - "ILeadRoutingService registered as Scoped in DI — injected into services that need to route leads at creation time"
  - "Round-robin uses stable OrderBy(u => u.Id) for consistent agent ordering across pointer advances per D-13"
  - "Territory routing leaves lead unassigned (returns null) if dimension value not in map per D-16"
  - "ConfigureRoutingEndpoint implements upsert — one routing config per tenant per D-15"
  - "LeadRoutingService takes TenantDbContext as parameter (not injected) to share the same transaction context with callers"

patterns-established:
  - "Task endpoints follow CreateTaskEndpoint pattern: validate request, check parent entity exists, create, save, return Created/Ok"
  - "Routing service takes db context as parameter not via DI to share transaction context with callers"

requirements-completed:
  - PIPE-02
  - PIPE-03

duration: 4min
completed: 2026-03-22
---

# Phase 4 Plan 04: Task Management and Lead Routing Summary

**Task CRUD endpoints (POST/GET/PUT) for lead-linked tasks and round-robin/territory lead routing service with upsert config endpoints**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-22T05:18:24Z
- **Completed:** 2026-03-22T05:22:00Z
- **Tasks:** 2
- **Files modified:** 9

## Accomplishments

- 3 task management endpoints: create task linked to lead, list tasks by assignee/lead, update task status/priority
- ILeadRoutingService interface and LeadRoutingService with strict round-robin (D-13) and territory routing (D-14)
- Territory routing returns null for unmatched dimension values per D-16 (leave unassigned)
- 2 routing configuration endpoints: POST upsert and GET retrieve (one config per tenant per D-15)
- Registered ILeadRoutingService in DI and all 5 new endpoints in Endpoints.cs

## Task Commits

Each task was committed atomically:

1. **Task 1: Task management endpoints (PIPE-02)** - `ba4fd5b` (feat)
2. **Task 2: Lead routing service and configuration endpoints (PIPE-03)** - `e02bc60` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `IronMonkey.ApiService/Features/Leads/Pipeline/Tasks/CreateTaskEndpoint.cs` - POST /api/leads/{leadId}/tasks with title/priority/assignee validation
- `IronMonkey.ApiService/Features/Leads/Pipeline/Tasks/ListTasksEndpoint.cs` - GET /api/tasks filtered by assignedTo or leadId, ordered by DueDate then Priority
- `IronMonkey.ApiService/Features/Leads/Pipeline/Tasks/UpdateTaskEndpoint.cs` - PUT /api/tasks/{taskId} updates status/priority/description/assignee
- `IronMonkey.ApiService/Features/Leads/Pipeline/Routing/ILeadRoutingService.cs` - Interface: Task<Guid?> GetNextAssigneeAsync returning null for no-match
- `IronMonkey.ApiService/Features/Leads/Pipeline/Routing/LeadRoutingService.cs` - Round-robin with RoundRobinPointer and territory map lookup via TerritoryMapJson
- `IronMonkey.ApiService/Features/Leads/Pipeline/Routing/ConfigureRoutingEndpoint.cs` - POST /api/routing-config upsert with strategy/dimension enum validation
- `IronMonkey.ApiService/Features/Leads/Pipeline/Routing/GetRoutingConfigEndpoint.cs` - GET /api/routing-config returns current config or 404
- `IronMonkey.ApiService/Endpoints.cs` - Added 5 new endpoint registrations in MapLeadsEndpoints
- `IronMonkey.ApiService/ConfigureServices.cs` - Registered ILeadRoutingService and Routing namespace using

## Decisions Made

- LeadRoutingService takes `TenantDbContext db` as parameter (not DI injection) so callers can share the same db context transaction when assigning leads
- Round-robin uses `OrderBy(u => u.Id)` for deterministic agent ordering — stable across calls per D-13
- Territory routing deserializes `TerritoryMapJson` as `Dictionary<string, string>` where values are agent GUIDs
- Custom field territory routing uses `lead.CustomFields.Values.TryGetValue(config.CustomFieldKey, ...)` following existing CustomFieldValues pattern

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Added ITenantService Common.Auth using directive**
- **Found during:** Task 1 (CreateTaskEndpoint)
- **Issue:** Plan code used `using IronMonkey.ApiService.Common;` but ITenantService is in `IronMonkey.ApiService.Common.Auth` namespace
- **Fix:** Added `using IronMonkey.ApiService.Common.Auth;` to all new endpoint files
- **Files modified:** All 5 new endpoint files
- **Verification:** Build succeeded with 0 errors
- **Committed in:** ba4fd5b and e02bc60

---

**Total deviations:** 1 auto-fixed (missing using directive for ITenantService)
**Impact on plan:** Trivial fix, no scope creep.

## Issues Encountered

- Parallel agent (04-03) modified Endpoints.cs concurrently to add Kanban/States namespaces. Build succeeded once 04-03's files were present on disk (parallel wave coordination worked correctly).

## Known Stubs

None - all endpoints are fully wired to LeadTask and RoutingConfig entities via TenantDbContext.

## Next Phase Readiness

- Task management (PIPE-02) complete: create/list/update tasks linked to leads
- Lead routing service (PIPE-03) ready to be called at lead creation time in CreateLeadEndpoint
- ILeadRoutingService registered in DI, callers can inject and call GetNextAssigneeAsync
- Routing config endpoints allow admin to configure strategy before routing goes live

---
*Phase: 04-pipeline-workflow-engine*
*Completed: 2026-03-22*
