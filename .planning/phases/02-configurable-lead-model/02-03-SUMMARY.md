---
phase: 02-configurable-lead-model
plan: "03"
subsystem: api-endpoints
tags: [minimal-api, leads, custom-fields, pipeline-stages, tenant-isolation, crud]

# Dependency graph
requires:
  - phase: 02-02
    provides: PipelineStage, CustomFieldDefinition, Lead entities with LeadSource enum and PipelineStageId FK
  - phase: 01-multi-tenancy-foundation
    provides: ITenantService, ITenantDbContextFactory, IEndpoint pattern
provides:
  - POST /api/custom-fields — create custom field definitions with type validation and options enforcement
  - GET /api/custom-fields — list tenant's field definitions ordered by name
  - POST /api/pipeline-stages — create pipeline stage with unique order check (409 on conflict)
  - GET /api/pipeline-stages — list active stages ordered by Order (IsActive filter via global query filter)
  - PUT /api/pipeline-stages/{id} — update name and order with uniqueness check excluding current stage
  - POST /api/leads — create lead with LeadSource enum parsing and PipelineStageId validation
affects:
  - 02-06 (integration tests can now exercise these endpoints)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "ITenantService.GetCurrentTenantId() + GetConnectionStringAsync() to resolve per-tenant TenantDbContext"
    - "ITenantDbContextFactory.CreateForTenant(connectionString, tenantId) pattern in all 6 endpoints"
    - "Enum.TryParse<T>(ignoreCase: true) for LeadSource and CustomFieldType string-to-enum parsing"
    - "Results<Created<T>, BadRequest<string>, Conflict<string>> typed results for write endpoints"
    - "Global query filter (IsActive on PipelineStage) respected automatically — no manual Where clause needed"

key-files:
  created:
    - IronMonkey.ApiService/Features/Leads/CustomFields/CreateCustomFieldEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/CustomFields/ListCustomFieldsEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/PipelineStages/CreatePipelineStageEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/PipelineStages/ListPipelineStagesEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/PipelineStages/UpdatePipelineStageEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/CreateLeadEndpoint.cs
  modified:
    - IronMonkey.ApiService/Endpoints.cs

key-decisions:
  - "ITenantDbContextFactory used directly (not ITenantService.GetTenantDbContextAsync) — ITenantService only provides GetCurrentTenantId() and GetConnectionStringAsync(); factory pattern matches LoginEndpoint precedent"
  - "MapLeadsEndpoints() extension method added to Endpoints.cs — consistent with existing Map*Endpoints() pattern; all 6 endpoints called individually via Map(app)"
  - "Enum.TryParse with ignoreCase:true — tolerates 'manual'/'MANUAL' input; aligns with plan spec for LeadSource and CustomFieldType"

requirements: [LEAD-01, LEAD-02, LEAD-03]

# Metrics
duration: 3min
completed: 2026-03-21
---

# Phase 02 Plan 03: Lead API Endpoints Summary

**6 Minimal API endpoints for custom field definitions, pipeline stages, and lead creation — all using ITenantDbContextFactory for per-tenant DB isolation and RequireAuthorization() for JWT enforcement**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-21T07:33:08Z
- **Completed:** 2026-03-21T07:36:19Z
- **Tasks:** 2
- **Files modified:** 7

## Accomplishments

- 5 endpoint files: CreateCustomField, ListCustomFields, CreatePipelineStage, ListPipelineStages, UpdatePipelineStage under Features/Leads/
- CreateLeadEndpoint with LeadSource enum string parsing and PipelineStageId existence validation
- Endpoints.cs updated with MapLeadsEndpoints() method registering all 6 new endpoints without disturbing Phase 1 registrations
- All 6 endpoints require JWT authorization via RequireAuthorization()
- Tenant isolation via ITenantDbContextFactory.CreateForTenant() — each request resolves tenant DB from JWT claims

## Task Commits

Each task was committed atomically:

1. **Task 1: Custom field + pipeline stage CRUD endpoints** - `a80d184` (feat)
2. **Task 2: CreateLead endpoint + register all endpoints** - `9656363` (feat)

## Files Created/Modified

- `IronMonkey.ApiService/Features/Leads/CustomFields/CreateCustomFieldEndpoint.cs` - POST /api/custom-fields with CustomFieldType enum parsing; requires Options for Dropdown/MultiSelect
- `IronMonkey.ApiService/Features/Leads/CustomFields/ListCustomFieldsEndpoint.cs` - GET /api/custom-fields ordered by FieldName; tenant isolation via global query filter
- `IronMonkey.ApiService/Features/Leads/PipelineStages/CreatePipelineStageEndpoint.cs` - POST /api/pipeline-stages with Order uniqueness check (409 Conflict on duplicate)
- `IronMonkey.ApiService/Features/Leads/PipelineStages/ListPipelineStagesEndpoint.cs` - GET /api/pipeline-stages ordered by Order; IsActive filter via global query filter
- `IronMonkey.ApiService/Features/Leads/PipelineStages/UpdatePipelineStageEndpoint.cs` - PUT /api/pipeline-stages/{id} with Order uniqueness excluding current stage
- `IronMonkey.ApiService/Features/Leads/CreateLeadEndpoint.cs` - POST /api/leads with LeadSource parsing and PipelineStageId validation
- `IronMonkey.ApiService/Endpoints.cs` - Added MapLeadsEndpoints() and using statements for all 3 new namespaces

## Decisions Made

1. **ITenantDbContextFactory used directly** — The plan referenced `ITenantService.GetTenantDbContextAsync()` but the actual interface only exposes `GetCurrentTenantId()` and `GetConnectionStringAsync()`. The established pattern (matching LoginEndpoint) is to call both and pass results to `ITenantDbContextFactory.CreateForTenant()`. Applied consistently across all 6 endpoints.

2. **MapLeadsEndpoints() extension method** — Rather than inlining 6 endpoint registrations in MapEndpoints(), added a dedicated private method following the existing Map*Endpoints() convention, keeping Endpoints.cs organized.

3. **Enum.TryParse with ignoreCase: true** — Tolerates case-insensitive input ("manual", "MANUAL", "Manual") for both LeadSource and CustomFieldType. Consistent with user-facing API conventions.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] ITenantService interface mismatch corrected**
- **Found during:** Task 1 implementation
- **Issue:** Plan specified `ITenantService.GetTenantDbContextAsync()` but the actual ITenantService interface has `GetCurrentTenantId()` + `GetConnectionStringAsync()`. No `GetTenantDbContextAsync()` method exists.
- **Fix:** Used the two-step pattern: `tenantService.GetCurrentTenantId()` then `tenantService.GetConnectionStringAsync()` then `dbContextFactory.CreateForTenant(connectionString, tenantId)`. This matches exactly how LoginEndpoint and TenantProvisioningService resolve tenant contexts.
- **Files modified:** All 6 endpoint files
- **Commit:** `a80d184` (Task 1), `9656363` (Task 2)

## Known Stubs

None — all endpoints are fully wired to the database entities established in Plan 02-02.

## Issues Encountered

- IronMonkey.Web pre-existing CS0542 error in CreatePermission.razor — unrelated to this plan, excluded from build scope (ApiService-only build used for verification).

## User Setup Required

None.

## Next Phase Readiness

- All 6 lead-related endpoints are functional and ready for integration testing in Plan 02-06
- Plan 02-04 (duplicate detection) operates on separate files — no conflicts

## Self-Check: PASSED
