---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: unknown
stopped_at: Completed 03-lead-ingestion/03-03-PLAN.md
last_updated: "2026-03-21T18:35:49.722Z"
progress:
  total_phases: 5
  completed_phases: 2
  total_plans: 19
  completed_plans: 15
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-19)

**Core value:** Any business can configure their complete lead management workflow without writing code
**Current focus:** Phase 02 — configurable-lead-model

## Current Position

Phase: 3
Plan: Not started

## Performance Metrics

**Velocity:**

- Total plans completed: 0
- Average duration: —
- Total execution time: 0 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| - | - | - | - |

**Recent Trend:**

- Last 5 plans: none yet
- Trend: —

*Updated after each plan completion*
| Phase 01-multi-tenancy-foundation P01 | 10 | 2 tasks | 7 files |
| Phase 01-multi-tenancy-foundation P02 | 28 | 3 tasks | 16 files |
| Phase 01-multi-tenancy-foundation P03 | 10 | 3 tasks | 17 files |
| Phase 01-multi-tenancy-foundation P04 | 7 | 3 tasks | 9 files |
| Phase 01-multi-tenancy-foundation P05 | 428 | 2 tasks | 10 files |
| Phase 01-multi-tenancy-foundation P06 | 15 | 1 tasks | 3 files |
| Phase 01-multi-tenancy-foundation P06 | 15 | 2 tasks | 4 files |
| Phase 02-configurable-lead-model P01 | 8 | 2 tasks | 5 files |
| Phase 02-configurable-lead-model P02 | 16 | 2 tasks | 12 files |
| Phase 02-configurable-lead-model P03 | 3 | 2 tasks | 7 files |
| Phase 02-configurable-lead-model P04 | 35 | 2 tasks | 8 files |
| Phase 02-configurable-lead-model P05 | 15 | 2 tasks | 6 files |
| Phase 02-configurable-lead-model P05 | 15 | 2 tasks | 6 files |
| Phase 03-lead-ingestion P01 | 4 | 2 tasks | 7 files |
| Phase 03-lead-ingestion P02 | 6 | 2 tasks | 9 files |
| Phase 03-lead-ingestion P03 | 6 | 2 tasks | 8 files |

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- DB-per-tenant isolation: Maximum data isolation, compliance-friendly (pending resolution)
- Billing model: Deferred — not finalized (pending resolution)
- [Phase 01-multi-tenancy-foundation]: xUnit 2.9.3 test project with TestContainers PostgreSQL fixture (postgres:15-alpine) and Fact(Skip) stubs as Wave 0 test scaffold
- [Phase 01-02]: Npgsql 10.0.1 used (not 9.x) — 9.x binary-incompatible with EF Core 10.x at runtime
- [Phase 01-02]: AppDbContext kept as CentralDbContext shim for backward compat during endpoint migration
- [Phase 01-02]: EF Core upgraded to 10.0.5 to match dotnet-ef tool version, avoiding migration generation errors
- [Phase 01-multi-tenancy-foundation]: UserTenantIndex central table: email->tenantId mapping enables O(1) login tenant lookup without scanning all tenant DBs
- [Phase 01-multi-tenancy-foundation]: LoginEndpoint 3-step flow: central email index lookup -> provisioned tenant check -> BCrypt password verify in tenant DB
- [Phase 01-multi-tenancy-foundation]: Migration-first role seeding: EF tenant migration already seeds all roles — TenantProvisioningService loads existing Admin role by name from DB rather than inserting duplicate
- [Phase 01-multi-tenancy-foundation]: TenantProvisioningService uses raw NpgsqlConnection for CREATE DATABASE — EF Core cannot run cross-database DDL; idempotent via pg_database existence check
- [Phase 01-multi-tenancy-foundation]: Provisioning endpoint pattern: ITenantProvisioningService.ProvisionTenantAsync catches InvalidOperationException and returns 400 BadRequest for not-approved or missing signup requests
- [Phase 01-05]: Hangfire.PostgreSql 1.21.1 used — latest stable at execution time
- [Phase 01-05]: OutboxMessage.Published property added alongside ProcessedOnUtc — MarkAsPublished() sets both
- [Phase 01-05]: MigrateAllTenantsEndpoint continues on partial failure — returns FailedCount and per-tenant error list
- [Phase 01-multi-tenancy-foundation]: OutboxMessage.Published migration added inline — MigrateAsync() in TenantProvisioningService requires full migration history alignment
- [Phase 01-multi-tenancy-foundation]: TenantIsolationTests load-or-insert role seeding: EF migration seeds roles during MigrateAsync() — test seed must check before inserting to avoid unique constraint violation
- [Phase 02-configurable-lead-model]: Wave 0 stub plan: 02-06 chosen as implementation target for all 17 Phase 2 stubs, consistent with Phase 1 naming convention
- [Phase 02-configurable-lead-model]: CustomFieldValues stored via HasConversion JSONB value converter (not OwnsOne.ToJson) — EF Core 10 ToJson does not support Dictionary<string,object?> navigation properties
- [Phase 02-configurable-lead-model]: FuzzySharp 2.0.0 used (not 1.11.0) — 1.11.0 does not exist on NuGet; 2.0.0 resolved automatically
- [Phase 02-configurable-lead-model]: LeadSource enum stored as string via HasConversion<string>() for human-readable DB values and migration safety
- [Phase 02-configurable-lead-model]: ITenantDbContextFactory used directly in endpoints — ITenantService.GetTenantDbContextAsync does not exist; two-step pattern: GetCurrentTenantId + GetConnectionStringAsync + CreateForTenant
- [Phase 02-configurable-lead-model]: MapLeadsEndpoints() extension method added to Endpoints.cs — registers all 6 Phase 2 lead endpoints following existing Map*Endpoints() convention
- [Phase 02-configurable-lead-model]: FuzzySharp 2.0.0 used for fuzzy name matching — 1.11.0 does not exist on NuGet
- [Phase 02-configurable-lead-model]: Fuzzy name match uses IgnoreQueryFilters with explicit TenantId+IsDeleted filter — prevents cross-tenant data exposure
- [Phase 02-configurable-lead-model]: Snapshot-before-mutation pattern for LeadMerge audit — serialize source/target before any field changes
- [Phase 02-configurable-lead-model]: Moq ITenantService stub for service-layer integration tests — returns test DB connection string enabling direct invocation of DuplicateDetectionService and LeadMergeService against real PostgreSQL
- [Phase 02-configurable-lead-model]: LeadMergeService JSONB IsModified fix — HasConversion value converters require db.Entry(entity).Property(...).IsModified = true after mutating dictionary reference
- [Phase 02-configurable-lead-model]: Moq ITenantService stub for DuplicateDetectionService/LeadMergeService tests — services take ITenantService to get connection string; stub returns test DB conn string, enabling direct service invocation without HTTP layer
- [Phase 02-configurable-lead-model]: Unique DB per test using GUID suffix — prevents test interference when running in parallel; each test gets its own PostgreSQL database within the shared TestContainers instance
- [Phase 02-configurable-lead-model]: LeadMergeService.CustomFields.IsModified fix — HasConversion JSONB value converters require explicit db.Entry(entity).Property(...).IsModified = true after mutating dictionary reference
- [Phase 02-configurable-lead-model]: ListPipelineStages active filter uses explicit .Where(p => p.IsActive) — global query filter only covers IsDeleted; IsActive is a separate business concept from soft-delete
- [Phase 03-lead-ingestion]: 03-06-PLAN chosen as implementation target for all Phase 3 stubs — consistent with Phase 1 and Phase 2 naming convention
- [Phase 03-lead-ingestion]: ApiKey in CentralDbContext for O(1) tenant lookup by key hash without prior tenant context
- [Phase 03-lead-ingestion]: WebForm in CentralDbContext so form token lookup works before tenant is resolved
- [Phase 03-lead-ingestion]: CreateLeadViaApiEndpoint uses AllowAnonymous + X-Api-Key header (not JWT RequireAuthorization) — external callers have no JWT; tenant resolved via IApiKeyService

### Pending Todos

None yet.

### Blockers/Concerns

- Phase 2: Dynamic field indexing performance needs validation — PostgreSQL JSONB indexing with 100+ fields per tenant. Plan for load testing in Phase 2.
- Phase 1: SQLite → PostgreSQL migration required before DB-per-tenant can be implemented. Current EF config uses SQLite.

## Session Continuity

Last session: 2026-03-21T18:35:49.716Z
Stopped at: Completed 03-lead-ingestion/03-03-PLAN.md
Resume file: None
