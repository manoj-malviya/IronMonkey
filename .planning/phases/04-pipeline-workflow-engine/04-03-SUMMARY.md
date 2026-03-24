---
phase: 04-pipeline-workflow-engine
plan: 03
subsystem: api
tags: [state-machine, kanban, pipeline, ef-core, minimal-api]

requires:
  - phase: 04-02
    provides: StageTransition entity and DB table, Lead.MoveToPipelineStage(), PipelineStage with StageType

provides:
  - IStateValidationService interface and StateValidationService implementation
  - ConfigureTransitionsEndpoint: POST /api/stage-transitions
  - ListTransitionsEndpoint: GET /api/stage-transitions
  - GetKanbanBoardEndpoint: GET /api/kanban with 20-lead virtual scroll
  - MoveLeadEndpoint: POST /api/leads/{leadId}/move with state machine validation

affects:
  - 04-05 (routing validation may reuse IStateValidationService pattern)
  - 04-06 (workflow engine triggers may include stage transitions)

tech-stack:
  added: []
  patterns:
    - State validation service: interface + implementation injected into endpoints via DI
    - FindAsync with object[] for keyed lookups in EF Core
    - Virtual scroll pattern: Take(20) + TotalCount + HasMore flag per column
    - Transition validation: read-only check before mutation, separate db contexts

key-files:
  created:
    - IronMonkey.ApiService/Features/Leads/Pipeline/States/IStateValidationService.cs
    - IronMonkey.ApiService/Features/Leads/Pipeline/States/StateValidationService.cs
    - IronMonkey.ApiService/Features/Leads/Pipeline/States/ConfigureTransitionsEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Pipeline/States/ListTransitionsEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Pipeline/Kanban/GetKanbanBoardEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Pipeline/Kanban/MoveLeadEndpoint.cs
  modified:
    - IronMonkey.ApiService/Endpoints.cs
    - IronMonkey.ApiService/ConfigureServices.cs

key-decisions:
  - "IStateValidationService uses separate DB context from mutation — validation reads StageTransitions, MoveLeadEndpoint opens a second db context for the actual update"
  - "StateValidationService returns null on success, error string on failure — enables inline error propagation without exceptions"
  - "GetKanbanBoardEndpoint queries each stage sequentially (not parallel) to avoid EF Core DbContext thread safety issues"

requirements-completed: [PIPE-01, PIPE-05]

duration: 350s
completed: "2026-03-22"
---

# Phase 4 Plan 03: State Machine Validation and Kanban Board API Summary

**State machine validation service against StageTransitions DB table, Kanban board with 20-lead virtual scroll per column, and lead move endpoint with D-08 inline error format**

## Performance

- **Duration:** 350 seconds (~6 min)
- **Started:** 2026-03-22T05:18:16Z
- **Completed:** 2026-03-22T05:24:06Z
- **Tasks:** 2
- **Files modified:** 8 (6 created, 2 modified)

## Accomplishments

- State machine validation service reads StageTransitions table, builds D-08 error message listing allowed stage names when transition is blocked
- Kanban board endpoint groups leads by active pipeline stage with 20-lead virtual scroll (Take(20) + HasMore + TotalCount), filters by agent/source/date, includes DaysInStage calculation
- Move lead endpoint validates transition before mutating, returns structured 400 with "Cannot move from [A] to [B]. Allowed: [C, D]" format
- POST /api/stage-transitions creates transition rules between stages; GET lists all with stage names resolved

## Task Commits

Each task was committed atomically:

1. **Task 1: State validation service and transition configuration endpoints** - `f19c118` (feat)
2. **Task 2: Kanban board and move lead endpoints** - `6940859` (feat)

**Plan metadata:** (pending final commit)

## Files Created/Modified

- `IronMonkey.ApiService/Features/Leads/Pipeline/States/IStateValidationService.cs` - Interface with Task<string?> ValidateTransitionAsync
- `IronMonkey.ApiService/Features/Leads/Pipeline/States/StateValidationService.cs` - Queries StageTransitions, builds D-08 error with allowed stage names
- `IronMonkey.ApiService/Features/Leads/Pipeline/States/ConfigureTransitionsEndpoint.cs` - POST /api/stage-transitions, validates both stages exist, rejects duplicate transitions
- `IronMonkey.ApiService/Features/Leads/Pipeline/States/ListTransitionsEndpoint.cs` - GET /api/stage-transitions, includes FromStage/ToStage names via Include()
- `IronMonkey.ApiService/Features/Leads/Pipeline/Kanban/GetKanbanBoardEndpoint.cs` - GET /api/kanban, 20-lead virtual scroll per column, filters D-04
- `IronMonkey.ApiService/Features/Leads/Pipeline/Kanban/MoveLeadEndpoint.cs` - POST /api/leads/{leadId}/move, IStateValidationService injection, D-08 error on invalid transition
- `IronMonkey.ApiService/Endpoints.cs` - Added 4 new endpoint registrations in MapLeadsEndpoints
- `IronMonkey.ApiService/ConfigureServices.cs` - Added IStateValidationService -> StateValidationService scoped registration

## Decisions Made

1. **Separate DB contexts for validation and mutation** — StateValidationService opens its own context for the read-only transition check; MoveLeadEndpoint opens a second context for the actual `MoveToPipelineStage` mutation. Avoids object tracking conflicts and matches established codebase pattern.
2. **StateValidationService returns null on success** — Simpler than throwing exceptions or returning a Result<T>; enables `if (error != null) return TypedResults.BadRequest(error)` pattern inline.
3. **Sequential stage queries in GetKanbanBoard** — EF Core DbContext is not thread-safe; `foreach` over stages with `await` inside is correct. A parallel approach would require a DbContext per stage.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed TaskStatus ambiguous reference in UpdateTaskEndpoint.cs**
- **Found during:** Task 1 (build verification after creating States files)
- **Issue:** `IronMonkey.Data.Entities.TaskStatus` was ambiguous with `System.Threading.Tasks.TaskStatus` causing CS0104 compile error
- **Fix:** Replaced `TaskStatus` with fully qualified `IronMonkey.Data.Entities.TaskStatus` in the two usages
- **Files modified:** `IronMonkey.ApiService/Features/Leads/Pipeline/Tasks/UpdateTaskEndpoint.cs`
- **Verification:** Build succeeded with 0 errors after fix
- **Committed in:** f19c118 (part of Task 1 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 - blocking build error in parallel plan's file)
**Impact on plan:** Fix was necessary for build to succeed; UpdateTaskEndpoint.cs was created by parallel plan 04-04 with an ambiguous reference that appeared during this plan's build.

## Issues Encountered

- Parallel plans (04-04) had already modified Endpoints.cs and ConfigureServices.cs before this plan ran, requiring reading the current state before editing. The TaskStatus ambiguity was the only actual code issue.

## Known Stubs

None — all 6 files are fully implemented with real DB queries, error handling, and correct response types.

## Next Phase Readiness

- State machine validation is ready for use by 04-06 workflow engine (can call IStateValidationService or query StageTransitions directly)
- Kanban board ready for frontend integration in IronMonkey.Web Blazor components
- Move lead endpoint ready for optimistic drag-drop implementation (D-02)

## Self-Check: PASSED

- `IronMonkey.ApiService/Features/Leads/Pipeline/States/IStateValidationService.cs` — FOUND
- `IronMonkey.ApiService/Features/Leads/Pipeline/States/StateValidationService.cs` — FOUND
- `IronMonkey.ApiService/Features/Leads/Pipeline/States/ConfigureTransitionsEndpoint.cs` — FOUND
- `IronMonkey.ApiService/Features/Leads/Pipeline/States/ListTransitionsEndpoint.cs` — FOUND
- `IronMonkey.ApiService/Features/Leads/Pipeline/Kanban/GetKanbanBoardEndpoint.cs` — FOUND
- `IronMonkey.ApiService/Features/Leads/Pipeline/Kanban/MoveLeadEndpoint.cs` — FOUND
- Commit f19c118 — Task 1: state validation service and transition endpoints
- Commit 6940859 — Task 2: Kanban board and move lead endpoints

---
*Phase: 04-pipeline-workflow-engine*
*Completed: 2026-03-22*
