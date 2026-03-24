---
phase: 03-lead-ingestion
plan: 06
subsystem: lead-ingestion
tags: [wiring, rate-limiting, duplicate-detection, endpoints, di-registration]
dependency_graph:
  requires: [03-03, 03-04, 03-05]
  provides: [INGST-01, INGST-02, INGST-03, INGST-04]
  affects: [IronMonkey.ApiService]
tech_stack:
  added:
    - "System.Threading.RateLimiting (built-in .NET 7+ rate limiting)"
  patterns:
    - "FixedWindowRateLimiter partitioned by X-Api-Key header for REST API (100/min)"
    - "FixedWindowRateLimiter partitioned by route token for web forms (10/min)"
    - "RequireRateLimiting() per-endpoint policy binding"
    - "MapIngestionEndpoints() extension method isolating ingestion route registration"
    - "Manual lead creation: duplicate warning response (200 OK + WasCreated=false) before commit"
    - "ForceCreate=true bypass for duplicate warning on manual entry (D-13)"
key_files:
  created: []
  modified:
    - IronMonkey.ApiService/ConfigureServices.cs
    - IronMonkey.ApiService/ConfigureApp.cs
    - IronMonkey.ApiService/Endpoints.cs
    - IronMonkey.ApiService/Features/Leads/CreateLeadEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Ingestion/Api/CreateLeadViaApiEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Ingestion/WebForm/SubmitWebFormEndpoint.cs
decisions:
  - "RouteValues on HttpContext.Request (not HttpContext) for rate limiter partition key — HttpContext does not expose RouteValues directly"
  - "CsvImportJob registered as Scoped (not Transient) — matches existing service registrations and Hangfire's scoped job activation pattern"
  - "MapIngestionEndpoints extracted from MapLeadsEndpoints for clarity — ingestion endpoints are a distinct concern from core lead CRUD"
metrics:
  duration: "~10 minutes"
  completed: "2026-03-21"
  tasks_completed: 2
  files_created: 0
  files_modified: 6
---

# Phase 3 Plan 6: Wire Ingestion Layer Summary

**One-liner:** Rate-limited ingestion wiring with duplicate-warning manual lead creation and extracted MapIngestionEndpoints registration.

## What Was Built

All Phase 3 services registered in DI, rate limiting middleware configured with two named policies, all ingestion endpoints registered via a dedicated `MapIngestionEndpoints()` method, and `CreateLeadEndpoint` enhanced with duplicate detection returning a warning response before creating.

### Task 1: ConfigureServices.cs + ConfigureApp.cs

**Service registrations added:**
- `builder.Services.AddScoped<CsvImportJob>()` — Hangfire job class (was missing from prior plans)
- `builder.AddRateLimiter()` — rate limiting middleware with two named policies

**AddRateLimiter private method:**
- `"api-key-limit"` — `FixedWindowRateLimiter`, 100 permits/minute, partitioned by `X-Api-Key` header value (falls back to `anonymous-{IP}` if no key)
- `"form-token-limit"` — `FixedWindowRateLimiter`, 10 permits/minute, partitioned by `{token}` route value
- `RejectionStatusCode = 429` on both

**ConfigureApp.cs middleware order:**
```
UseAuthentication → UseAuthorization → UseRateLimiter → MapEndpoints
```

### Task 2: Endpoints.cs + Endpoint enhancements

**Endpoints.cs:**
- Extracted `MapIngestionEndpoints()` from inline `MapLeadsEndpoints()` code
- Registers all 11 ingestion endpoints in grouped sections (API key management, external REST, CSV, web forms)
- Called from `MapEndpoints()` after `MapLeadsEndpoints()`

**CreateLeadViaApiEndpoint.cs:**
- Added `.RequireRateLimiting("api-key-limit")` to Map() — 100 requests/min per API key

**SubmitWebFormEndpoint.cs:**
- Added `.RequireRateLimiting("form-token-limit")` to Map() — 10 submissions/min per form token

**CreateLeadEndpoint.cs — duplicate warning per D-13:**
- `Request` extended with `bool ForceCreate = false`
- `DuplicateMatch` record added: `(Guid LeadId, string FullName, string Email, int ConfidenceScore)`
- `Response` extended with `List<DuplicateMatch> Duplicates` and `bool WasCreated`
- Handle method now accepts `IDuplicateDetectionService duplicateDetection`
- After stage validation, calls `FindCandidatesAsync` for email + mobile + name
- If duplicates found AND `ForceCreate=false`: returns `200 OK` with `WasCreated=false`, empty `Id=Guid.Empty`, full duplicate list — caller can display warning and re-submit with `ForceCreate=true`
- If `ForceCreate=true` or no duplicates: creates lead, returns `201 Created` with `WasCreated=true` and empty duplicate list

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed HttpContext.RouteValues not found**
- **Found during:** Task 1 rate limiter implementation
- **Issue:** Plan showed `httpContext.RouteValues["token"]` but `RouteValues` is not on `HttpContext` — it is on `HttpRequest`
- **Fix:** Changed to `httpContext.Request.RouteValues["token"]?.ToString()`
- **Files modified:** ConfigureServices.cs
- **Commit:** ce0d1a6

### Pre-existing Issues (Out of Scope)

- `IronMonkey.Web/Components/SuperAdmin/CreatePermission.razor` has a CS0542 error (member name same as enclosing type). Pre-existing before this plan. `IronMonkey.ApiService` and `IronMonkey.Tests` both build with 0 errors.

## Known Stubs

None — all services fully registered, rate limiting active, duplicate detection wired with real `IDuplicateDetectionService`.

## Self-Check: PASSED

Files modified:
- FOUND: IronMonkey.ApiService/ConfigureServices.cs
- FOUND: IronMonkey.ApiService/ConfigureApp.cs
- FOUND: IronMonkey.ApiService/Endpoints.cs
- FOUND: IronMonkey.ApiService/Features/Leads/CreateLeadEndpoint.cs
- FOUND: IronMonkey.ApiService/Features/Leads/Ingestion/Api/CreateLeadViaApiEndpoint.cs
- FOUND: IronMonkey.ApiService/Features/Leads/Ingestion/WebForm/SubmitWebFormEndpoint.cs

Commits:
- FOUND: ce0d1a6 (feat(03-06): register CsvImportJob and rate limiting in DI)
- FOUND: 28a9ee2 (feat(03-06): wire ingestion endpoints and add duplicate detection to manual lead creation)
