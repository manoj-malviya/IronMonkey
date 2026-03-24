# Phase 5: Activity & Reporting - Research

**Researched:** 2026-03-24
**Domain:** Activity Logging, Dashboard Aggregation, Real-time Data Freshness
**Confidence:** HIGH

## Summary

Phase 5 requires three distinct technical capabilities: automatic activity tracking of all entity changes, server-side aggregation for dashboard metrics, and strategies for data freshness and performance at scale. The research confirms that EF Core SaveChanges interceptors are the standard approach for activity logging, direct LINQ queries with proper PostgreSQL indexing meet the "no materialized views in v1" requirement, and existing project patterns (global query filters, entity configurations, tenant isolation) apply directly to new ActivityLog entity and dashboard endpoints.

**Primary recommendation:** Implement ActivityLog entity with SaveChangesInterceptor for automatic change tracking (no manual instrumentation), use direct LINQ queries with strategic B-tree and composite indexes on PipelineStageId/LeadSource/AssignedToUserId/CreatedAt columns, and add Amount field to Opportunity entity for deal value aggregation.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01:** EF Core SaveChanges interceptor + ActivityLog entity — automatically captures all entity changes during save. Stores actor (UserId), action type, entity type, entity ID, old/new values as JSON. No manual instrumentation per endpoint
- **D-02:** ActivityLog stored in tenant DB — follows existing per-tenant isolation pattern. Global query filter on TenantId applied automatically
- **D-03:** All system events + manual notes — stage transitions, field changes, task creation/completion, assignment changes, merge events, workflow rule firings, and user-added notes
- **D-04:** Chronological order, newest first — standard reverse-chronological timeline
- **D-05:** Filterable by event type — filter bar with toggles for: field changes, stage transitions, tasks, notes, assignments, system actions
- **D-06:** Paginated timeline — 20 events per page, "Load more" button. No infinite scroll on timeline
- **D-07:** Direct LINQ queries with server-side aggregation — no materialized views in v1. Simpler implementation, acceptable performance with proper indexes
- **D-08:** Proper indexes on PipelineStageId, LeadSource, AssignedToUserId, CreatedAt columns to support dashboard queries
- **D-09:** Leads per stage as horizontal bar or column chart — grouped by PipelineStage, showing count per stage
- **D-10:** Total deal value from Opportunity.Amount — sum grouped by stage. Leads without converted opportunity contribute $0
- **D-11:** Stage type awareness — ClosedWon/ClosedLost stages visually distinct from Active stages (per Phase 4 D-07 stage types)
- **D-12:** Conversion rate = leads reaching ClosedWon / total leads entering pipeline — calculated per stage, per source, and per time period
- **D-13:** Preset time periods with custom date range — Today, This Week, This Month, This Quarter, This Year. Custom date picker as fallback. Default: This Month
- **D-14:** Breakdown dimensions — by stage (funnel view), by lead source (Manual, Import, Api, WebForm), by time period (trend line)
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

### Deferred Ideas (OUT OF SCOPE)
- SignalR real-time push for live activity feed — future enhancement
- Custom report builder with filters, grouping, export — explicitly out of scope (v2)
- Dashboard data export (CSV, PDF) — future enhancement
- Scheduled email digest reports — future enhancement after communications phase
- Dashboard widget customization / drag-drop layout — future enhancement
- ActivityLog archival/cleanup scheduled job — add when data volume warrants it
- Comparative period analysis (this month vs last month) — future enhancement
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| ACTV-01 | Each lead has a unified activity timeline showing all interactions and changes | SaveChangesInterceptor enables automatic change tracking; ActivityLog entity with reverse-chronological ordering and pagination (20/page); filterable by event type |
| REPT-01 | Dashboard shows pipeline overview (leads per stage, total value) | Direct LINQ aggregation (Count, Sum on Opportunity.Amount) with grouping by PipelineStageId; stage type awareness for visual distinction; requires Amount field on Opportunity entity (Phase 5 schema addition) |
| REPT-02 | Dashboard shows conversion rates by stage, source, and time period | Conversion rate formula: leads reaching ClosedWon / total leads entering pipeline; breakdown by PipelineStageId, LeadSource, and CreatedAt date ranges; preset periods (Today/Week/Month/Quarter/Year) + custom date picker |
| REPT-03 | Dashboard shows agent performance metrics (leads handled, tasks completed, conversion rate) | Aggregation by AssignedToUserId: count distinct leads, count completed tasks (LeadTask.Status=Completed), conversion rate; sortable table; reuses time period selector from REPT-02 |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| EF Core SaveChangesInterceptor | 10.0.5 | Automatic change tracking for ActivityLog | Official EF Core interceptor pattern for auditing; simpler than override SaveChangesAsync; no manual instrumentation per endpoint |
| PostgreSQL JSONB | 15-alpine | Store old/new field values in ActivityLog | Native PostgreSQL support; EF Core HasConversion() uses JSONB transparently; supports flexible change payloads |
| LINQ aggregation (Count, Sum, GroupBy) | Net10.0 | Dashboard metrics calculation | Zero-cost abstraction over SQL; easier than raw SQL; keeps logic in application layer for testability |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Stateless | 5.20.1 | Activity event enumeration (already in project) | Ensure consistent event type values across timeline filtering |
| Chart.js / DevExpress Charts | varies | Frontend dashboard visualization | Client-side rendering; DevExpress for commercial, Chart.js wrapper for open source |
| PostgreSQL B-tree indexes | native | Column indexing for dashboard queries | Standard for filtering on CreatedAt, PipelineStageId, LeadSource, AssignedToUserId |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| SaveChangesInterceptor | SaveChangesAsync override in TenantDbContext | Interceptor: cleaner separation, reusable; Override: simpler but mixes concerns. Decision: Interceptor is locked (D-01) |
| Direct LINQ queries | Materialized views in PostgreSQL | Direct: simpler v1, always fresh; Views: faster after refresh but stale data, refresh overhead. Decision: Direct queries (D-07) |
| JSONB for old/new values | Text/XML columns | JSONB: flexible, indexed, efficient; Text: simple but unindexed. Decision: JSONB for consistency with existing CustomFieldValues pattern |
| B-tree indexes | GIN indexes on JSONB | B-tree: smaller, faster writes; GIN: supports complex queries on JSONB. Decision: B-tree on scalar columns, GIN not needed unless querying inside JSON (Phase 5 doesn't) |

**Installation:**
```bash
# No new packages required — SaveChangesInterceptor is built-in to EF Core 10.0.5
# JSONB is native PostgreSQL feature
# Indexes are created via EF Core migrations
# Chart libraries (Chart.js or DevExpress) installed during UI implementation
```

**Version verification:** EF Core 10.0.5 (already in project), Npgsql 10.0.1 (already matches). PostgreSQL 15-alpine (Testcontainers config). No version changes needed for Phase 5 core logic.

## Architecture Patterns

### Recommended Project Structure
```
IronMonkey.Data/
├── Entities/
│   └── ActivityLog.cs              # New: Stores all change events
├── Migrations/Tenant/
│   └── YYYYMMDD_Phase5_ActivityLog.cs  # New: ActivityLog table + indexes
├── EntityConfigurations/
│   └── ActivityLogConfiguration.cs # New: Global query filter, indexes

IronMonkey.ApiService/
├── Features/
│   ├── Activity/
│   │   ├── Timeline/
│   │   │   └── GetLeadActivityTimelineEndpoint.cs  # New: Paginated timeline
│   │   └── IActivityTrackingService.cs             # New: Interface for capturing events
│   ├── Reports/
│   │   ├── Pipeline/
│   │   │   └── GetPipelineDashboardEndpoint.cs     # New: Lead count + deal value per stage
│   │   ├── Conversion/
│   │   │   └── GetConversionDashboardEndpoint.cs   # New: Conversion rates by stage/source/period
│   │   └── Performance/
│   │       └── GetAgentPerformanceDashboardEndpoint.cs  # New: Agent metrics by period
│   └── Leads/
│       └── [existing endpoints]
├── Interceptors/
│   └── ActivityChangeInterceptor.cs  # New: ISaveChangesInterceptor implementation
├── Endpoints.cs                      # Modified: Add MapActivityEndpoints, MapReportingEndpoints
└── ConfigureServices.cs              # Modified: Register IActivityTrackingService
```

### Pattern 1: SaveChanges Interceptor for Activity Logging

**What:** Implement ISaveChangesInterceptor to intercept SaveChangesAsync and capture entity changes before they commit. Extract old/new values from DbContext.ChangeTracker, serialize as JSON, create ActivityLog entity, add to context.

**When to use:** Every change to Lead, Contact, Opportunity, LeadTask, PipelineStage, CustomField, etc. No endpoint needs manual logging calls.

**Example:**
```csharp
// Source: https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors
public class ActivityChangeInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context as TenantDbContext;
        if (context == null) return result;

        var tenantId = context.TenantId; // Requires exposing TenantId property
        var actorId = _userContext.GetCurrentUserId(); // From IUserContext claim

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.Entity is BaseTenantEntity && e.State != EntityState.Unchanged)
            .ToList();

        foreach (var entry in entries)
        {
            var entity = (BaseTenantEntity)entry.Entity;
            var eventType = entry.State switch
            {
                EntityState.Added => "Created",
                EntityState.Modified => "Updated",
                EntityState.Deleted => "Deleted",
                _ => "Unknown"
            };

            // Capture old/new values
            var oldValues = entry.State == EntityState.Modified
                ? entry.OriginalValues.Properties
                    .ToDictionary(p => p.Name, p => entry.OriginalValues[p] as object)
                : null;

            var newValues = entry.State != EntityState.Deleted
                ? entry.CurrentValues.Properties
                    .ToDictionary(p => p.Name, p => entry.CurrentValues[p] as object)
                : null;

            var activityLog = ActivityLog.Create(
                tenantId,
                entity.GetType().Name,
                entity.Id,
                eventType,
                actorId,
                oldValues,
                newValues
            );

            context.ActivityLogs.Add(activityLog);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
```

### Pattern 2: Dashboard Aggregation with Direct LINQ Queries

**What:** Use LINQ GroupBy, Count, Sum, and Where to calculate dashboard metrics server-side. Execute as single SQL query (EF Core translates to GROUP BY). No materialized views.

**When to use:** REPT-01 (pipeline), REPT-02 (conversion), REPT-03 (agent performance). Real-time metrics acceptable.

**Example (Pipeline Dashboard):**
```csharp
// Source: Phase 4 GetKanbanBoardEndpoint pattern
var pipelineMetrics = await db.Leads
    .Where(l => l.CreatedAt >= startDate && l.CreatedAt <= endDate)
    .GroupBy(l => l.PipelineStageId)
    .Select(g => new
    {
        StageId = g.Key,
        LeadCount = g.Count(),
        DealValue = db.Opportunities
            .Where(o => db.Leads
                .Where(lead => lead.PipelineStageId == g.Key)
                .Select(lead => lead.ConvertedOpportunityId)
                .Contains(o.Id))
            .Sum(o => (decimal?)o.Amount) ?? 0m,
        IsConverted = g.Count(l => l.IsConverted),
        ConversionRate = (decimal)g.Count(l => l.IsConverted) / g.Count()
    })
    .ToListAsync();
```

**Note:** Opportunity.Amount must be added to schema (see "Don't Hand-Roll" for schema design).

### Pattern 3: Chronological Timeline with Pagination

**What:** Query ActivityLog ordered by CreatedAt DESC, apply type filters via enum-based Where, paginate with Skip/Take (20 per page).

**When to use:** ACTV-01 lead detail page. Support client-side "Load more" button.

**Example:**
```csharp
public record ActivityEventDto(
    Guid Id, string EventType, string EntityType, DateTime OccurredAt,
    Dictionary<string, object?>? OldValues, Dictionary<string, object?>? NewValues,
    string ActorName);

var events = await db.ActivityLogs
    .Where(a => a.LeadId == leadId)
    .Where(a => !selectedTypes.Any() || selectedTypes.Contains(a.EventType))
    .OrderByDescending(a => a.CreatedAt)
    .Skip(pageNumber * 20)
    .Take(20)
    .Select(a => new ActivityEventDto(
        a.Id,
        a.EventType,
        a.EntityType,
        a.CreatedAt,
        a.OldValues,
        a.NewValues,
        a.Actor.FirstName + " " + a.Actor.LastName
    ))
    .ToListAsync();

// Return with HasMore = totalCount > (pageNumber + 1) * 20
```

### Anti-Patterns to Avoid
- **Logging inside endpoints:** Every endpoint logging changes is unmaintainable; use interceptor instead (D-01)
- **Materializing all data in memory:** Don't fetch all leads/opportunities then aggregate in LINQ-to-Objects; let LINQ-to-SQL push aggregation to database
- **Storing raw JSON without type info:** Use TypeNameHandling in serialization for deserializing to correct event types later
- **No indexes on dashboard query columns:** Dashboard endpoints will scan full tables without indexes; D-08 requires strategic indexes upfront
- **Conversion rate = IsConverted field only:** Must calculate from stage transitions or ClosedWon lead count, not just flag (D-12)

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Change tracking for every entity | Custom property-by-property change detection | SaveChangesInterceptor + DbContext.ChangeTracker | Interceptor provides property-level old/new values automatically; avoids comparison logic in every service |
| Storing flexible change payloads | Custom XML/text serialization | EF Core JSONB via HasConversion() | JSONB is queryable, indexable, integrates with EF Core value converters |
| Dashboard aggregation | In-memory materialization (fetch all leads, group in C#) | Direct LINQ-to-SQL with Count/Sum/GroupBy | Database executes GROUP BY/SUM natively; network transfer minimized; caching via connection pooling |
| Time period filtering | Manual DateTime calculation (Is date between 1st of month?) | PostgreSQL date_trunc, EF Core arithmetic on CreatedAt | Database handles timezone-aware date math; reusable across multiple dashboards |
| Conversion rate calculation | Multiple queries (count all, count converted, divide) | Single aggregation with division: `converted / total` in Select clause | Single query, server-side computation, avoids precision loss |
| Activity timeline ordering | "Load all then sort in C#" | OrderByDescending(a => a.CreatedAt).Take(20) | Database index on CreatedAt DESC supports efficient scan; only 20 rows transfer |

**Key insight:** Phase 5's scalability depends on pushing aggregation and filtering to PostgreSQL via LINQ-to-SQL. In-memory aggregation works for <1000 leads but fails at 10K+. Strategic indexing (D-08) is the difference between 50ms and 5s queries.

## Code Examples

Verified patterns from existing codebase and EF Core documentation:

### ActivityLog Entity
```csharp
// Source: Modeled after OutboxMessage entity pattern (TenantDbContext.cs)
using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public sealed class ActivityLog : BaseTenantEntity
{
    private ActivityLog() { }

    public Guid LeadId { get; private set; }
    public Guid ActorId { get; private set; }
    public string EventType { get; private set; } = string.Empty; // "Created", "Updated", "Deleted", "StageMoved", etc.
    public string EntityType { get; private set; } = string.Empty; // "Lead", "LeadTask", "Contact", etc.
    public string EntityId { get; private set; } = string.Empty; // ID of changed entity (may differ from LeadId for nested entities)

    // JSONB columns for flexibility (following CustomFieldValues pattern from Phase 2)
    public Dictionary<string, object?>? OldValues { get; private set; }
    public Dictionary<string, object?>? NewValues { get; private set; }

    public User Actor { get; private set; } = null!;
    public Lead Lead { get; private set; } = null!;

    public static ActivityLog Create(
        Guid tenantId,
        Guid leadId,
        Guid actorId,
        string eventType,
        string entityType,
        string entityId,
        Dictionary<string, object?>? oldValues = null,
        Dictionary<string, object?>? newValues = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeadId = leadId,
            ActorId = actorId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues
        };
}
```

### GetLeadActivityTimelineEndpoint
```csharp
// Source: Modeled after GetKanbanBoardEndpoint (pattern in existing codebase)
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Activity.Timeline;

public class GetLeadActivityTimelineEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/leads/{leadId}/activity", Handle)
        .WithSummary("Get lead activity timeline (20 events/page, filterable by type)")
        .WithTags("Activity")
        .RequireAuthorization();

    public record ActivityEventDto(
        Guid Id,
        string EventType,
        string EntityType,
        DateTime OccurredAt,
        Dictionary<string, object?>? OldValues,
        Dictionary<string, object?>? NewValues,
        string ActorName);

    public record TimelineResponse(
        List<ActivityEventDto> Events,
        int PageNumber,
        int PageSize,
        bool HasMore);

    private static async Task<Ok<TimelineResponse>> Handle(
        Guid leadId,
        int page = 0,
        string? typeFilter = null,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var query = db.ActivityLogs.Where(a => a.LeadId == leadId);

        // D-05: Filter by event type
        if (!string.IsNullOrEmpty(typeFilter))
            query = query.Where(a => a.EventType == typeFilter);

        var pageSize = 20; // D-06
        var totalCount = await query.CountAsync(cancellationToken);

        var events = await query
            .OrderByDescending(a => a.CreatedAt) // D-04: Newest first
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(a => new ActivityEventDto(
                a.Id,
                a.EventType,
                a.EntityType,
                a.CreatedAt,
                a.OldValues,
                a.NewValues,
                a.Actor.FirstName + " " + a.Actor.LastName))
            .ToListAsync(cancellationToken);

        var hasMore = totalCount > (page + 1) * pageSize;

        return TypedResults.Ok(new TimelineResponse(events, page, pageSize, hasMore));
    }
}
```

### GetPipelineDashboardEndpoint (Aggregation Pattern)
```csharp
// Source: Combines GetKanbanBoardEndpoint aggregation pattern with dashboard requirements
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Reports.Pipeline;

public class GetPipelineDashboardEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/dashboard/pipeline", Handle)
        .WithSummary("Get pipeline overview: leads per stage, total deal value")
        .WithTags("Reporting")
        .RequireAuthorization();

    public record StageMetricDto(
        Guid StageId,
        string StageName,
        string StageType,
        int LeadCount,
        decimal TotalDealValue);

    public record PipelineResponse(List<StageMetricDto> Stages);

    private static async Task<Ok<PipelineResponse>> Handle(
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        // D-07: Direct LINQ query, no materialized views
        var stageMetrics = await db.PipelineStages
            .Where(s => s.IsActive)
            .Select(stage => new StageMetricDto(
                stage.Id,
                stage.Name,
                stage.StageType.ToString(), // D-11: Stage type for visual distinction
                db.Leads.Count(l => l.PipelineStageId == stage.Id), // D-09
                db.Opportunities
                    .Where(o => db.Leads
                        .Where(l => l.PipelineStageId == stage.Id && l.ConvertedOpportunityId == o.Id)
                        .Any())
                    .Sum(o => (decimal?)o.Amount) ?? 0m)) // D-10
            .OrderBy(s => s.StageName)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PipelineResponse(stageMetrics));
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Manual change logging in endpoints | SaveChanges interceptor (D-01) | EF Core 7.0+ | Centralized, automatic, zero boilerplate; enables ACTV-01 |
| Materialized views for dashboards | Direct LINQ queries + indexes (D-07) | Modern EF Core + fast networks | Simpler v1, always fresh; v2 can add caching if needed |
| XML/Text change payloads | JSONB with EF Core HasConversion (D-01) | PostgreSQL 9.4+, EF Core 3.0+ | Queryable, indexable, flexible schema |
| Manual stage transition tracking | StageTransition entity + ActivityLog event | Phase 4 + Phase 5 | Two sources of truth allow both event feed and state validation |

**Deprecated/outdated:**
- Audit tables separate from application entities: Modern approach is interceptors + single ActivityLog table
- Hard-coded event types: Phase 5 uses open-ended EventType string (allows adding new events without DB migration)
- Page-per-dashboard: v1 uses single aggregation endpoint per dashboard (D-07 direct queries); v2 can add real-time via SignalR

## Open Questions

1. **How to handle ActivityLog for non-Lead entities (e.g., Contact change)?**
   - Current plan: ActivityLog.LeadId is nullable for Contact/Opportunity changes, or store only LeadId for lead-related changes
   - Question: Should system show Contact activity on Lead detail, or separate?
   - Recommendation: Phase 5 v1 tracks Lead and direct children (LeadTask, Assignment) only. Contact/Opportunity activity deferred to Phase 5 v2 or separate contact timeline.

2. **What counts as "time from assignment to first activity" (D-16)?**
   - Question: First ActivityLog event? First LeadTask creation? First note?
   - Recommendation: Query for min(ActivityLog.CreatedAt) where EventType != "Assigned" after AssignedToUserId change. Fallback to LeadTask creation if no activity found.

3. **Opportunity.Amount: Should it be nullable, default to 0, or required?**
   - Current Decision: Add as nullable decimal, default null. Migration will backfill existing opportunities with 0 if needed.
   - Recommendation: Verify with Phase 2 Opportunity entity implementation (currently no Amount field) before migration.

4. **Should ActivityLog track changes to ActivityLog itself (to prevent infinite loop)?**
   - Recommendation: Interceptor must check entity type and exclude ActivityLog entries from change capture (similar to OutboxMessage handling).

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| PostgreSQL | Dashboard queries, ActivityLog storage | ✓ | 15-alpine (Testcontainers) | — |
| EF Core 10.0.5 | SaveChangesInterceptor, LINQ aggregation | ✓ | 10.0.5 | — |
| JSONB (PostgreSQL) | Old/new value storage | ✓ | Native in PostgreSQL 15 | — |
| Chart.js or DevExpress | Dashboard UI rendering | — (Phase 5 frontend scope) | — | Later phase or V2 UI framework |

**Missing dependencies with no fallback:**
- None for Phase 5 core logic (Activity tracking + aggregation endpoints)

**Missing dependencies with fallback:**
- Chart.js (frontend): Can display raw JSON data as tables until chart library is selected

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 with Moq 4.20.72 |
| Test Container | Testcontainers.PostgreSql 4.3.0 (postgres:15-alpine) |
| Config file | None — xUnit auto-discovers tests via attributes |
| Quick run command | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ActivityTests" -v normal` |
| Full suite command | `dotnet test IronMonkey.Tests -v normal` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| ACTV-01 | ActivityLog captures all Lead field changes | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ActivityLogTests.Activity_log_records_field_changes" -x` | ❌ Wave 0 |
| ACTV-01 | Timeline returns events in reverse-chronological order | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ActivityTimelineTests.Timeline_returns_newest_first" -x` | ❌ Wave 0 |
| ACTV-01 | Pagination works: 20 events per page, HasMore flag | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ActivityTimelineTests.Pagination_20_per_page" -x` | ❌ Wave 0 |
| ACTV-01 | Event type filtering works (field changes, stage transitions, etc.) | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ActivityTimelineTests.Filter_by_event_type" -x` | ❌ Wave 0 |
| REPT-01 | Pipeline dashboard aggregates leads per stage | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~PipelineDashboardTests.Pipeline_shows_lead_count_per_stage" -x` | ❌ Wave 0 |
| REPT-01 | Pipeline dashboard sums deal value from Opportunity.Amount | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~PipelineDashboardTests.Pipeline_shows_total_deal_value" -x` | ❌ Wave 0 |
| REPT-01 | Stage types (ClosedWon/ClosedLost) visually distinct (data field present) | Unit | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~PipelineDashboardTests.Stage_type_included_in_response" -x` | ❌ Wave 0 |
| REPT-02 | Conversion rate = closed won / total entered pipeline | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ConversionDashboardTests.Conversion_rate_calculation_correct" -x` | ❌ Wave 0 |
| REPT-02 | Breakdown by stage shows funnel structure | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ConversionDashboardTests.Breakdown_by_stage" -x` | ❌ Wave 0 |
| REPT-02 | Breakdown by lead source (Manual, Import, Api, WebForm) | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ConversionDashboardTests.Breakdown_by_source" -x` | ❌ Wave 0 |
| REPT-02 | Time period filtering (Today, Week, Month, Quarter, Year, Custom) | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ConversionDashboardTests.Time_period_" -x` | ❌ Wave 0 |
| REPT-03 | Agent metrics: leads assigned count | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~AgentPerformanceTests.Agent_leads_assigned_count" -x` | ❌ Wave 0 |
| REPT-03 | Agent metrics: tasks completed count | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~AgentPerformanceTests.Agent_tasks_completed" -x` | ❌ Wave 0 |
| REPT-03 | Agent metrics: conversion rate per agent | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~AgentPerformanceTests.Agent_conversion_rate" -x` | ❌ Wave 0 |
| REPT-03 | Agent metrics: avg response time (assignment to first activity) | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~AgentPerformanceTests.Agent_response_time" -x` | ❌ Wave 0 |
| REPT-03 | Agent table sortable by columns | Unit | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~AgentPerformanceTests.Agent_table_sortable" -x` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~Activity" -v normal` (quick smoke test: <30s)
- **Per wave merge:** `dotnet test IronMonkey.Tests -v normal` (full suite: ~5 min with Testcontainers startup)
- **Phase gate:** Full suite green + manual verification of dashboard UI before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `IronMonkey.Tests/Integration/ActivityLogTests.cs` — covers ACTV-01 change capture, filtering, pagination (3-4 test methods)
- [ ] `IronMonkey.Tests/Integration/ActivityTimelineTests.cs` — covers ACTV-01 timeline endpoint (reverse-chronological, HasMore)
- [ ] `IronMonkey.Tests/Integration/PipelineDashboardTests.cs` — covers REPT-01 lead count + deal value aggregation
- [ ] `IronMonkey.Tests/Integration/ConversionDashboardTests.cs` — covers REPT-02 conversion rate by stage/source/period
- [ ] `IronMonkey.Tests/Integration/AgentPerformanceTests.cs` — covers REPT-03 agent metrics, response time
- [ ] `IronMonkey.Data/Entities/ActivityLog.cs` — entity definition (schema, properties, Create factory)
- [ ] `IronMonkey.Data/Migrations/Tenant/YYYYMMDD_Phase5_ActivityLog.cs` — EF migration: ActivityLog table + indexes per D-08
- [ ] `IronMonkey.ApiService/Interceptors/ActivityChangeInterceptor.cs` — SaveChangesInterceptor implementation
- [ ] `IronMonkey.Data/Entities/Opportunity.cs` — add Amount field (decimal?) for REPT-01 deal value
- [ ] `IronMonkey.Data/Migrations/Tenant/YYYYMMDD_AddOpportunityAmount.cs` — backfill existing opportunities
- [ ] Framework install: No new packages; xUnit + Testcontainers already in place

*(All Wave 0 items required before Wave 1 implementation can start)*

## Common Pitfalls

### Pitfall 1: Ignoring Opportunities Without Associated Leads in REPT-01
**What goes wrong:** Pipeline dashboard shows only opportunities tied to non-deleted, current-stage leads. Orphaned opportunities (from merged/deleted leads) don't contribute to deal value.
**Why it happens:** JOIN on Lead.ConvertedOpportunityId requires lead to exist and pass global query filters
**How to avoid:** Test with merged leads; verify deal value calculation includes opportunities from deleted lead scenarios
**Warning signs:** Dashboard deal value drops after lead merge; total doesn't match Opportunity table sum

### Pitfall 2: Conversion Rate = IsConverted Flag (Incorrect)
**What goes wrong:** Using Lead.IsConverted directly as conversion rate numerator; misses stage transitions that indicate true conversion
**Why it happens:** Developers mistake a single boolean for complex business logic; D-12 requires stage-aware calculation
**How to avoid:** Conversion rate = COUNT(leads where final_stage.StageType = ClosedWon) / COUNT(all leads in pipeline). Validate against manually calculated test data.
**Warning signs:** Conversion rate doesn't match manual calculation; agents report discrepancy between dashboard and reality

### Pitfall 3: ActivityLog Infinite Loop (Interceptor Captures Own Writes)
**What goes wrong:** SaveChangesInterceptor adds ActivityLog entities, which triggers SaveChangesInterceptor again, which adds more ActivityLog entities, etc.
**Why it happens:** ChangeTracker.Entries() includes newly-added ActivityLog entities if not filtered
**How to avoid:** Exclude ActivityLog entity type from interceptor: `where e.Entity is not ActivityLog`
**Warning signs:** Test hangs or crashes; SaveChangesAsync never completes; ActivityLog row count explodes (hundreds added per save)

### Pitfall 4: No Index on CreatedAt for Dashboard Queries
**What goes wrong:** Pipeline dashboard query scans entire Leads table; query takes 5+ seconds with 100K leads
**Why it happens:** D-08 requires indexes but developer forgets migration; no index exists on CreatedAt or PipelineStageId
**How to avoid:** Include migration with composite indexes: (PipelineStageId, CreatedAt) and (LeadSource, CreatedAt)
**Warning signs:** Dashboard API timeout (>30s); slow integration tests; production dashboards slow to load

### Pitfall 5: Materializing All Lead Data in Memory for Aggregation
**What goes wrong:** `db.Leads.ToList().GroupBy(l => l.PipelineStageId).Select(...)` loads all 100K leads into RAM
**Why it happens:** Developer forgets that ToList() is LINQ-to-Objects, not LINQ-to-SQL
**How to avoid:** Keep query as IQueryable<> until final aggregation; only call ToListAsync() on grouped result
**Warning signs:** Server RAM spikes; OutOfMemoryException; queries slow to start (data transfer time)

### Pitfall 6: ActivityLog JSON Serialization Loses Type Information
**What goes wrong:** Old/new values stored as JSON can't deserialize back to original types; all values become strings or nulls
**Why it happens:** Default JsonConvert.SerializeObject doesn't include type metadata
**How to avoid:** Use TypeNameHandling.All in JsonSerializerSettings (see TenantDbContext.cs pattern); test round-trip serialization
**Warning signs:** ActivityLog columns show all strings; date fields lose formatting; comparison logic fails

## Sources

### Primary (HIGH confidence)
- [Microsoft Learn: Interceptors - EF Core](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors) — SaveChangesInterceptor pattern, ISaveChangesInterceptor interface
- [PostgreSQL Documentation 18: JSON Types](https://www.postgresql.org/docs/current/datatype-json.html) — JSONB storage and native operators
- [EF Core Interceptors: SaveChangesInterceptor for Auditing](https://mehmetozkaya.medium.com/ef-core-interceptors-savechangesinterceptor-for-auditing-entities-in-net-8-microservices-6923190a03b9) — Practical implementation example for change tracking
- [Tracking Every Change: Using SaveChanges Interception for EF Core Auditing](https://www.woodruff.dev/tracking-every-change-using-savechanges-interception-for-ef-core-auditing/) — Chris Woodruff auditing pattern

### Secondary (MEDIUM confidence)
- [How to Use Materialized Views in PostgreSQL](https://oneuptime.com/blog/post/2026-01-25-use-materialized-views-postgresql/view) — Comparison of direct queries vs materialized views for dashboards (verifies D-07 tradeoff)
- [PostgreSQL JSONB Performance Guide: Indexing & Query Optimization](https://www.sitepoint.com/postgresql-jsonb-query-performance-indexing/) — GIN indexes for JSONB, performance considerations
- [Funnel Analysis and Conversion Metrics in SQL](https://www.fivetran.com/blog/funnel-analysis) — Conversion rate calculation formula (D-12 verification)
- [Running a Funnel Analysis in SQL](https://popsql.com/sql-templates/marketing/running-a-funnel-analysis) — SQL patterns for conversion dashboard
- [How to Index JSONB Data in PostgreSQL](https://www.tigerdata.com/learn/how-to-index-json-columns-in-postgresql) — Practical JSONB indexing strategies

### Tertiary (LOW confidence — referenced for context, not critical)
- [Blazor Charts - Beautiful & Interactive](https://www.devexpress.com/blazor/chart/) — Commercial chart component for Phase 5 UI (frontend selection deferred)
- [Creating Real-Time Charts with Blazor WebAssembly and SignalR](https://code-maze.com/creating-blazor-webassembly-signalr-charts/) — Real-time push strategy (deferred to Phase 5 v2, marked as deferred idea)
- [API Pagination Best Practices in REST API Design](https://www.speakeasy.com/api-design/pagination) — 20-item page size is standard, offset pagination suitable for dashboards (D-06 verification)

## Metadata

**Confidence breakdown:**
- Standard stack (SaveChangesInterceptor, JSONB, LINQ aggregation): **HIGH** — Verified via official EF Core docs + PostgreSQL docs + existing project patterns
- Architecture (timeline pagination, dashboard aggregation, interceptor pattern): **HIGH** — Pattern used in existing GetKanbanBoardEndpoint; interceptor pattern confirmed by Microsoft Learn
- Pitfalls (infinite loops, index absence, materialization): **HIGH** — Common EF Core gotchas verified by multiple auditing pattern sources
- Chart library selection: **MEDIUM** — DevExpress/Chart.js exist but frontend scope deferred; placeholder for future UI phase
- ActivityLog schema details: **MEDIUM** — Flexible (Claude's Discretion); implementation will refine during Wave 0

**Research date:** 2026-03-24
**Valid until:** 2026-04-24 (30 days; EF Core 10.x stable, PostgreSQL 15 stable)

---

*Phase: 05-activity-reporting*
*Research completed: 2026-03-24*
