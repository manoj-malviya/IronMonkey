---
phase: 01-multi-tenancy-foundation
plan: "05"
subsystem: background-jobs
tags: [hangfire, postgresql, outbox, tenant-isolation, migrations]
dependency_graph:
  requires: [01-04]
  provides: [hangfire-postgresql, ITenantRegistry, OutboxProcessingJob, MigrateAllTenantsEndpoint]
  affects: [IronMonkey.ApiService, IronMonkey.Data]
tech_stack:
  added:
    - Hangfire.Core 1.8.17
    - Hangfire.AspNetCore 1.8.17
    - Hangfire.PostgreSql 1.21.1
  patterns:
    - Explicit TenantId parameter pattern for background jobs
    - TDD (Red-Green) for OutboxProcessingJob tests
key_files:
  created:
    - IronMonkey.ApiService/BackgroundJobs/ITenantRegistry.cs
    - IronMonkey.ApiService/BackgroundJobs/TenantRegistry.cs
    - IronMonkey.ApiService/BackgroundJobs/OutboxProcessingJob.cs
    - IronMonkey.ApiService/Authentication/Endpoints/MigrateAllTenantsEndpoint.cs
  modified:
    - IronMonkey.ApiService/IronMonkey.ApiService.csproj
    - IronMonkey.ApiService/ConfigureServices.cs
    - IronMonkey.ApiService/ConfigureApp.cs
    - IronMonkey.ApiService/Endpoints.cs
    - IronMonkey.Data/Outbox/OutboxMessage.cs
    - IronMonkey.Data/Configurations/OutboxMessageConfiguration.cs
    - IronMonkey.Tests/Unit/HangfireJobTenantTests.cs
decisions:
  - "Hangfire.PostgreSql 1.21.1 used (not 1.20.x) — latest stable at time of execution"
  - "OutboxMessage.Published property added alongside existing ProcessedOnUtc — MarkAsPublished() sets both"
  - "HangfireAdminOnlyAuthFilter placed in ConfigureApp.cs — not a separate file (single small class)"
  - "MigrateAllTenantsEndpoint continues on partial failure — log error per tenant and report FailedCount in response"
metrics:
  duration_seconds: 428
  completed_date: "2026-03-20"
  tasks_completed: 2
  files_changed: 10
---

# Phase 01 Plan 05: Hangfire Background Jobs and Migration Orchestration Summary

**One-liner:** Hangfire with PostgreSQL storage, ITenantRegistry for explicit-TenantId job pattern, OutboxProcessingJob, and admin migration orchestration endpoint.

## What Was Built

### Hangfire Integration
- Registered Hangfire with PostgreSQL storage using the `CentralDb` connection string
- `HangfireServer` with `WorkerCount = ProcessorCount * 2`, queues: `["default", "tenant"]`
- Dashboard mounted at `/hangfire` behind `HangfireAdminOnlyAuthFilter` — requires authenticated Admin or SuperAdmin role

### ITenantRegistry / TenantRegistry
- `ITenantRegistry` interface: `GetConnectionStringAsync(tenantId)` and `GetAllProvisionedTenantsAsync()`
- `TenantRegistry` backed by `CentralDbContext` — queries `Tenants` table, filters on `IsProvisioned = true`
- Throws `InvalidOperationException` for unprovisioned/unknown tenants — Hangfire retries on exception

### OutboxProcessingJob
- `Guid tenantId` is the **first** parameter — serialized into Hangfire job payload, survives process restarts
- Resolves connection string from `ITenantRegistry.GetConnectionStringAsync(tenantId)` — no ambient state
- `[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 300])]`, `[Queue("tenant")]`
- Per-message error handling: logs and continues, rethrows registry errors for Hangfire retry

### MigrateAllTenantsEndpoint
- Route: `POST /admin/migrate-all` — requires authorization
- Gets all provisioned tenants via `ITenantRegistry.GetAllProvisionedTenantsAsync()`
- Applies `db.Database.MigrateAsync()` to each tenant DB
- Returns `{ MigratedCount, FailedCount, Errors }` — continues on partial failures

### OutboxMessage Changes (Rule 2 - Missing Functionality)
- Added `Published` property (bool, default false) and `MarkAsPublished()` to `OutboxMessage`
- `MarkAsPublished()` sets `Published = true` and `ProcessedOnUtc = DateTime.UtcNow`
- Updated `OutboxMessageConfiguration` to map `Published` with `HasDefaultValue(false)`

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Functionality] Added Published property and MarkAsPublished() to OutboxMessage**
- **Found during:** Task 2 (OutboxProcessingJob)
- **Issue:** Plan referenced `m.Published` and `message.MarkAsPublished()` but `OutboxMessage` only had `ProcessedOnUtc` (no `Published` bool property, no method)
- **Fix:** Added `Published { get; private set; }` and `MarkAsPublished()` method; updated `OutboxMessageConfiguration` to map the column with default false
- **Files modified:** `IronMonkey.Data/Outbox/OutboxMessage.cs`, `IronMonkey.Data/Configurations/OutboxMessageConfiguration.cs`
- **Commit:** ed4472b

**2. [Rule 3 - Blocking Issue] Added `using Microsoft.EntityFrameworkCore` to MigrateAllTenantsEndpoint**
- **Found during:** Task 2 (MigrateAllTenantsEndpoint build)
- **Issue:** `MigrateAsync()` is a relational extension method requiring `Microsoft.EntityFrameworkCore` using directive
- **Fix:** Added `using Microsoft.EntityFrameworkCore;` to the endpoint file
- **Files modified:** `IronMonkey.ApiService/Authentication/Endpoints/MigrateAllTenantsEndpoint.cs`
- **Commit:** ed4472b

## Test Results

```
Passed! Failed: 0, Passed: 2, Skipped: 0, Total: 2
```

- `HangfireJob_WhenEnqueued_TenantIdPassedToRegistry` — verified `GetConnectionStringAsync` called with exactly `tenantId`, never any other guid
- `HangfireJob_WhenTenantIdInvalid_ThrowsInvalidOperationException` — verified exception propagates for Hangfire retry

## Commits

| Hash    | Message |
|---------|---------|
| fd8e847 | feat(01-05): add Hangfire with PostgreSQL storage and admin dashboard |
| 6294ee9 | test(01-05): add failing tests for OutboxProcessingJob explicit TenantId |
| ed4472b | feat(01-05): implement OutboxProcessingJob and MigrateAllTenantsEndpoint |

## Self-Check

- [x] `IronMonkey.ApiService/BackgroundJobs/ITenantRegistry.cs` exists
- [x] `IronMonkey.ApiService/BackgroundJobs/TenantRegistry.cs` exists
- [x] `IronMonkey.ApiService/BackgroundJobs/OutboxProcessingJob.cs` exists
- [x] `IronMonkey.ApiService/Authentication/Endpoints/MigrateAllTenantsEndpoint.cs` exists
- [x] 2 tests pass
- [x] ApiService builds clean (0 errors)
