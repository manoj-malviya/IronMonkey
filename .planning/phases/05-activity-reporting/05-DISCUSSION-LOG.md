# Phase 5: Activity & Reporting - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-03-24
**Phase:** 05-activity-reporting
**Areas discussed:** Activity tracking approach, Timeline content & display, Dashboard data freshness, Time period & filtering, Deal value tracking
**Mode:** Auto (--auto flag — all choices selected automatically)

---

## Activity Tracking Approach

| Option | Description | Selected |
|--------|-------------|----------|
| EF Core SaveChanges interceptor + ActivityLog entity | Auto-captures all changes during save, stores actor/action/old/new as JSON | ✓ |
| Explicit domain events per action | Manual event emission at each mutation point | |
| Database triggers | PostgreSQL triggers for change capture | |

**User's choice:** [auto] EF Core SaveChanges interceptor + ActivityLog entity (recommended default)
**Notes:** Interceptor approach requires zero per-endpoint instrumentation. Follows existing outbox pattern in TenantDbContext.SaveChangesAsync.

---

## Timeline Content & Display

| Option | Description | Selected |
|--------|-------------|----------|
| All system events + manual notes | Stage transitions, field changes, tasks, assignments, merges, notes | ✓ |
| System events only (no manual notes) | Automated tracking only | |
| Curated events (subset) | Only major lifecycle events | |

**User's choice:** [auto] All system events + manual notes (recommended default)
**Notes:** Comprehensive timeline matches ACTV-01 requirement for "all interactions and changes." Newest-first, paginated (20/page), filterable by event type.

---

## Dashboard Data Freshness

| Option | Description | Selected |
|--------|-------------|----------|
| Direct LINQ queries | Real-time queries with server-side aggregation | ✓ |
| Materialized views | Pre-computed aggregates refreshed periodically | |
| Hybrid (cache layer) | Query with short-lived cache | |

**User's choice:** [auto] Direct LINQ queries with server-side aggregation (recommended default)
**Notes:** Simplest v1 approach. Performance sustained by proper indexes. Materialized views can be added later if needed.

---

## Time Period & Filtering

| Option | Description | Selected |
|--------|-------------|----------|
| Preset periods with custom date range | Today, This Week, This Month, This Quarter, This Year + custom picker | ✓ |
| Presets only (no custom) | Fixed period options only | |
| Custom date range only | Full flexibility, no presets | |

**User's choice:** [auto] Preset periods with custom date range (recommended default)
**Notes:** Default view: This Month. Same selector shared across conversion and agent performance dashboards.

---

## Deal Value Tracking

| Option | Description | Selected |
|--------|-------------|----------|
| Use Opportunity.Amount | Sum existing Opportunity amounts grouped by stage | ✓ |
| Add Value field to Lead | Direct value field on lead entity | |
| No value tracking in v1 | Count-only dashboards | |

**User's choice:** [auto] Use Opportunity.Amount (recommended default)
**Notes:** Opportunity entity already exists in tenant schema. Leads without converted opportunity contribute $0 to pipeline value.

---

## Claude's Discretion

- ActivityLog entity schema and JSON serialization format
- SaveChanges interceptor implementation details
- Dashboard endpoint response DTO shapes
- Chart-ready data format
- Index creation strategy
- Note entity design
- Aggregation query optimization

## Deferred Ideas

- SignalR real-time push for live activity feed
- Custom report builder (explicitly out of scope for v1)
- Dashboard data export (CSV, PDF)
- Scheduled email digest reports
- Dashboard widget customization
- ActivityLog archival/cleanup job
- Comparative period analysis
