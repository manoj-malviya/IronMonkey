# Phase 4: Pipeline & Workflow Engine - Context

**Gathered:** 2026-03-22
**Status:** Ready for planning

<domain>
## Phase Boundary

Users can manage leads through a visual pipeline with task tracking, assignment routing, and automated rules that act on lead state changes. Includes Kanban board UI, task management, lead routing configuration, workflow rule engine, and state machine enforcement. Does not include reporting dashboards or activity timeline (Phase 5).

</domain>

<decisions>
## Implementation Decisions

### Kanban Board Behavior
- **D-01:** Standard card info — name, email, phone, source badge, assigned agent, time in stage
- **D-02:** Optimistic drag-drop — card moves instantly, server validates in background. If transition is invalid, card snaps back with error toast
- **D-03:** Virtual scroll per column — each column shows first 20 leads, scrolls to load more. No pagination controls
- **D-04:** Basic filters only in v1 — filter by assigned agent, lead source, date range. Single filter bar above the board. Saved views deferred

### State Machine Configuration
- **D-05:** Linear by default, customize exceptions — stages are sequential by default (1 → 2 → 3...). Tenant adds or removes specific transitions. Least configuration needed
- **D-06:** No default transitions — tenant must configure all transitions before the pipeline is usable. Most explicit, no implicit behavior
- **D-07:** Typed stages — stages have a type: Entry (where new leads land), Active (working stages), Closed-Won, Closed-Lost. System behavior differs by type (closed stages remove from active board count)
- **D-08:** Inline error on invalid transition — card snaps back to original column, toast message says "Cannot move from [Stage A] to [Stage B]. Allowed: [Stage C, Stage D]."

### Workflow Rules Engine
- **D-09:** Single trigger → single action — "When [trigger], do [action]." Simple, predictable. Multiple actions require multiple rules
- **D-10:** In-app notifications only — notification bell/badge in the UI. No email delivery in v1
- **D-11:** Hangfire scheduled scan for time-elapsed triggers — background job runs every hour, checks all leads against time-based rules, fires matching actions. Batch processing, slight delay acceptable
- **D-12:** Toggle on/off — each rule has an IsActive flag. Disabled rules are preserved but don't fire

### Lead Routing & Assignment
- **D-13:** Strict round-robin rotation — Agent A, B, C, A, B, C... regardless of current load
- **D-14:** Tenant picks routing dimension — route based on lead source OR a custom field value. Tenant chooses which dimension drives routing
- **D-15:** One strategy per pipeline — pipeline uses either round-robin OR territory routing. No mixing, no conflicts
- **D-16:** Leave unassigned if no match — lead enters pipeline with no assignee. Shows up in "Unassigned" filter. Manager assigns manually

### Claude's Discretion
- Kanban board component architecture (Blazor Server components, SignalR for real-time updates)
- Notification entity design and bell/badge UI implementation
- Workflow rule evaluation order and conflict resolution
- Task entity schema and priority level definitions
- State machine transition storage format (adjacency list vs matrix)
- Round-robin pointer storage (per-pipeline counter in tenant DB)
- Hangfire job scheduling for hourly time-elapsed rule scan

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches for pipeline management and workflow automation.

</specifics>

<canonical_refs>
## Canonical References

No external specs — requirements are fully captured in decisions above and ROADMAP.md success criteria.

### Upstream phase artifacts
- `.planning/phases/01-multi-tenancy-foundation/01-CONTEXT.md` — Tenant resolution, JWT claims, Hangfire job patterns, outbox pattern
- `.planning/phases/03-lead-ingestion/03-CONTEXT.md` — Rate limiting patterns, duplicate detection integration
- `.planning/ROADMAP.md` §Phase 4 — Success criteria for PIPE-01 through PIPE-05
- `.planning/REQUIREMENTS.md` §Pipeline & Workflow — Requirement definitions

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `PipelineStage` entity with Name, Order, IsActive — extend with StageType enum (Entry, Active, ClosedWon, ClosedLost) and AllowedTransitions
- `Lead` entity with PipelineStageId FK — extend with AssignedToUserId for routing
- `CreatePipelineStageEndpoint` / `ListPipelineStagesEndpoint` / `UpdatePipelineStageEndpoint` — extend for transition configuration
- `OutboxProcessingJob` — template for workflow rule evaluation Hangfire job
- `IDuplicateDetectionService` — pattern for creating IWorkflowRuleEngine service
- `IronMonkey.Web` Blazor Server project — build Kanban board components here
- `ApiClient` in Web project — HTTP client for API calls from Blazor components

### Established Patterns
- Tenant DB access: `ITenantService.GetCurrentTenantId()` → `GetConnectionStringAsync()` → `ITenantDbContextFactory.CreateForTenant()`
- Endpoint pattern: `IEndpoint` with static `Map()`, nested Request/Response records, nested `RequestValidator`
- Background jobs: `[Queue("tenant")]`, TenantId as first parameter, resolve connection from `ITenantRegistry`
- Domain events via outbox pattern — workflow actions can emit domain events
- Global query filters: TenantId + IsDeleted enforced automatically on TenantDbContext

### Integration Points
- `TenantDbContext`: Add DbSets for Task, WorkflowRule, StageTransition, Notification, RoutingConfig
- `IronMonkey.Data/Migrations/Tenant/`: New migration for Phase 4 entities
- `Endpoints.cs`: Register pipeline management and workflow endpoints
- `ConfigureServices.cs`: Register IWorkflowRuleEngine, ILeadRoutingService, INotificationService
- `IronMonkey.Web/Components/`: Kanban board Blazor components, task management UI
- `IronMonkey.AppHost/AppHost.cs`: Re-enable Web frontend project reference

</code_context>

<deferred>
## Deferred Ideas

- Saved/named filter views for the Kanban board — future enhancement
- Email notifications from workflow rules — add when communications phase is built
- Weighted round-robin and load-balanced routing — future enhancement
- Priority-ordered routing rules with fallback chains — future enhancement
- Workflow rule execution log/history — future enhancement
- Chained conditions (AND/OR) on workflow rules — future enhancement
- Visual transition graph editor — future enhancement beyond list-based config

</deferred>

---

*Phase: 04-pipeline-workflow-engine*
*Context gathered: 2026-03-22*
