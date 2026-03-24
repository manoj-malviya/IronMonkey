# Phase 5: Activity & Reporting - Context

**Gathered:** 2026-03-24
**Status:** Ready for planning

<domain>
## Phase Boundary

Users can see everything that happened on a lead in one place (unified activity timeline) and understand pipeline health and team performance through dashboards. Includes: lead activity timeline, pipeline overview dashboard, conversion rate dashboard, and agent performance dashboard. Does not include real-time push updates (SignalR), custom report builder, or export functionality.

</domain>

<decisions>
## Implementation Decisions

### Activity Tracking Approach
- **D-01:** EF Core SaveChanges interceptor + ActivityLog entity — automatically captures all entity changes during save. Stores actor (UserId), action type, entity type, entity ID, old/new values as JSON. No manual instrumentation per endpoint
- **D-02:** ActivityLog stored in tenant DB — follows existing per-tenant isolation pattern. Global query filter on TenantId applied automatically

### Timeline Content & Display
- **D-03:** All system events + manual notes — stage transitions, field changes, task creation/completion, assignment changes, merge events, workflow rule firings, and user-added notes
- **D-04:** Chronological order, newest first — standard reverse-chronological timeline
- **D-05:** Filterable by event type — filter bar with toggles for: field changes, stage transitions, tasks, notes, assignments, system actions
- **D-06:** Paginated timeline — 20 events per page, "Load more" button. No infinite scroll on timeline

### Dashboard Data Freshness
- **D-07:** Direct LINQ queries with server-side aggregation — no materialized views in v1. Simpler implementation, acceptable performance with proper indexes
- **D-08:** Proper indexes on PipelineStageId, LeadSource, AssignedToUserId, CreatedAt columns to support dashboard queries

### Pipeline Dashboard (REPT-01)
- **D-09:** Leads per stage as horizontal bar or column chart — grouped by PipelineStage, showing count per stage
- **D-10:** Total deal value from Opportunity.Amount — sum grouped by stage. Leads without converted opportunity contribute $0
- **D-11:** Stage type awareness — ClosedWon/ClosedLost stages visually distinct from Active stages (per Phase 4 D-07 stage types)

### Conversion Dashboard (REPT-02)
- **D-12:** Conversion rate = leads reaching ClosedWon / total leads entering pipeline — calculated per stage, per source, and per time period
- **D-13:** Preset time periods with custom date range — Today, This Week, This Month, This Quarter, This Year. Custom date picker as fallback. Default: This Month
- **D-14:** Breakdown dimensions — by stage (funnel view), by lead source (Manual, Import, Api, WebForm), by time period (trend line)

### Agent Performance Dashboard (REPT-03)
- **D-15:** Per-agent metrics — leads handled (assigned count), tasks completed, conversion rate (leads reaching ClosedWon / total assigned)
- **D-16:** Sortable agent table — columns: Agent Name, Leads Assigned, Leads Converted, Conversion %, Tasks Completed, Avg Response Time (time from assignment to first activity)
- **D-17:** Same time period selector as conversion dashboard — reuse D-13 preset/custom pattern

### Claude's Discretion
- ActivityLog entity schema design and JSON serialization format for old/new values
- SaveChanges interceptor implementation details (EF Core interceptor vs override)
- Dashboard endpoint response DTO shapes
- Chart-ready data format (labels + values arrays vs structured objects)
- Index creation strategy (migration vs manual SQL)
- ActivityLog cleanup/archival strategy
- Note entity design (if separate from ActivityLog) or note as ActivityLog entry type
- Exact aggregation query optimization approach

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches for activity tracking and dashboard reporting.

</specifics>

<canonical_refs>
## Canonical References

No external specs — requirements are fully captured in decisions above and ROADMAP.md success criteria.

### Upstream phase artifacts
- `.planning/phases/01-multi-tenancy-foundation/01-CONTEXT.md` — Tenant resolution, JWT claims, Hangfire job patterns, outbox/domain event pattern
- `.planning/phases/04-pipeline-workflow-engine/04-CONTEXT.md` — Stage types (D-07), in-app notifications (D-10), workflow rule engine patterns
- `.planning/ROADMAP.md` §Phase 5 — Success criteria for ACTV-01, REPT-01, REPT-02, REPT-03
- `.planning/REQUIREMENTS.md` §Reporting and §Activity — Requirement definitions

### Key existing code references
- `IronMonkey.Data/TenantDbContext.cs` — SaveChangesAsync override with outbox pattern (lines 67-126), template for activity interceptor
- `IronMonkey.Data/Entities/Lead.cs` — Lead entity with Source, PipelineStageId, AssignedToUserId, IsConverted
- `IronMonkey.Data/Entities/PipelineStage.cs` — StageType enum (Entry, Active, ClosedWon, ClosedLost), IsTerminal property
- `IronMonkey.Data/Entities/LeadTask.cs` — Task entity with Status, Priority, AssignedToUserId for REPT-03
- `IronMonkey.Data/Entities/Opportunity.cs` — Amount field for deal value aggregation (REPT-01)
- `IronMonkey.Data/Entities/StageTransition.cs` — Existing transition tracking for timeline events
- `IronMonkey.Data/Entities/LeadMerge.cs` — Merge audit trail with JSON snapshots
- `IronMonkey.ApiService/Features/Leads/Pipeline/Kanban/GetKanbanBoardEndpoint.cs` — Aggregation query pattern reference

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `TenantDbContext.SaveChangesAsync()` with domain event interception — extend for activity logging
- `OutboxMessage` entity — template for ActivityLog entity design (JSON content, timestamps)
- `GetKanbanBoardEndpoint` — aggregation query pattern with grouping, filtering, pagination
- `Lead` entity with Source enum, PipelineStageId, AssignedToUserId, IsConverted — all fields needed for dashboard queries
- `PipelineStage.StageType` enum — ClosedWon/ClosedLost detection for conversion calculations
- `LeadTask` with Status/Priority/AssignedToUserId — agent performance metrics source
- `Opportunity.Amount` — deal value for pipeline dashboard
- `StageTransition` entity — existing stage change tracking
- `LeadMerge` entity — existing merge event tracking with JSON snapshots
- `INotificationService` — pattern for creating IActivityTrackingService

### Established Patterns
- Tenant DB access: `ITenantService.GetCurrentTenantId()` → `GetConnectionStringAsync()` → `ITenantDbContextFactory.CreateForTenant()`
- Endpoint pattern: `IEndpoint` with static `Map()`, nested Request/Response records, nested `RequestValidator`
- Background jobs: `[Queue("tenant")]`, TenantId as first parameter, resolve connection from `ITenantRegistry`
- Global query filters: TenantId + IsDeleted enforced automatically on TenantDbContext
- Domain events via outbox pattern — SaveChangesAsync intercepts and serializes events

### Integration Points
- `TenantDbContext`: Add DbSet for ActivityLog entity, extend SaveChangesAsync for activity capture
- `IronMonkey.Data/Migrations/Tenant/`: New migration for ActivityLog table + dashboard indexes
- `Endpoints.cs`: Register activity timeline and dashboard endpoints
- `ConfigureServices.cs`: Register IActivityTrackingService, IDashboardService
- Existing entities (Lead, LeadTask, PipelineStage, Opportunity) — query targets for dashboards, no schema changes needed

</code_context>

<deferred>
## Deferred Ideas

- SignalR real-time push for live activity feed — future enhancement
- Custom report builder with filters, grouping, export — explicitly out of scope (v2)
- Dashboard data export (CSV, PDF) — future enhancement
- Scheduled email digest reports — future enhancement after communications phase
- Dashboard widget customization / drag-drop layout — future enhancement
- ActivityLog archival/cleanup scheduled job — add when data volume warrants it
- Comparative period analysis (this month vs last month) — future enhancement

</deferred>

---

*Phase: 05-activity-reporting*
*Context gathered: 2026-03-24*
