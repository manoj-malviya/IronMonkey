---
phase: 03-lead-ingestion
plan: 03
subsystem: api-ingestion
tags: [api-keys, bcrypt, external-api, lead-ingestion, multi-tenant]

# Dependency graph
requires:
  - phase: 03-02
    provides: ApiKey entity in CentralDbContext with BCrypt hash storage

provides:
  - IApiKeyService interface (GenerateAsync, ValidateAsync, DeleteAsync, ListAsync)
  - ApiKeyService: BCrypt hash-then-verify, 32-byte random key generation
  - GenerateApiKeyEndpoint: POST /api/api-keys (JWT auth, plaintext key returned once)
  - ListApiKeysEndpoint: GET /api/api-keys (JWT auth, prefix only)
  - DeleteApiKeyEndpoint: DELETE /api/api-keys/{keyId} (JWT auth, soft-deactivation)
  - CreateLeadViaApiEndpoint: POST /api/external/leads (X-Api-Key header, no JWT)

affects:
  - 03-06 (wave 0 stubs for API auth tests — ApiLeadCreationTests, ApiAuthTests)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - X-Api-Key header authentication: AllowAnonymous endpoint validates header via IApiKeyService
    - BCrypt hash-then-verify: never store plaintext, 32-byte random base64url key
    - Tenant resolution without JWT: IApiKeyService.ValidateAsync returns TenantId from key hash
    - ITenantRegistry for connectionless tenant lookup: resolves DB connection string by tenantId

key-files:
  created:
    - IronMonkey.ApiService/Features/Leads/Ingestion/Api/IApiKeyService.cs
    - IronMonkey.ApiService/Features/Leads/Ingestion/Api/ApiKeyService.cs
    - IronMonkey.ApiService/Features/Leads/Ingestion/Api/GenerateApiKeyEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Ingestion/Api/ListApiKeysEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Ingestion/Api/DeleteApiKeyEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Ingestion/Api/CreateLeadViaApiEndpoint.cs
  modified:
    - IronMonkey.ApiService/ConfigureServices.cs
    - IronMonkey.ApiService/Endpoints.cs

key-decisions:
  - "IApiKeyService registered as Scoped in ConfigureServices — consistent with other services"
  - "CreateLeadViaApiEndpoint uses AllowAnonymous + X-Api-Key header validation instead of RequireAuthorization"
  - "ValidateAsync loads all active keys and BCrypt.Verify each — constant-time prevents timing attacks; prefix index optimization deferred to 03-06"
  - "ITenantRegistry (BackgroundJobs namespace) used for tenant connection string resolution without JWT context"

# Metrics
duration: 6min
completed: 2026-03-21
---

# Phase 3 Plan 03: API Key Service and External Lead Ingestion Summary

**BCrypt-based API key generation and validation with external lead ingestion endpoint (X-Api-Key auth) returning duplicate detection results**

## Performance

- **Duration:** ~6 min
- **Started:** 2026-03-21T18:27:45Z
- **Completed:** 2026-03-21T18:33:45Z
- **Tasks:** 2
- **Files modified:** 8

## Accomplishments
- Created IApiKeyService interface with GenerateAsync, ValidateAsync, DeleteAsync, ListAsync
- Created ApiKeyService with 32-byte random key generation (base64url), BCrypt hash storage (workFactor 10), constant-time BCrypt.Verify for validation, soft-delete via Deactivate()
- Created GenerateApiKeyEndpoint (POST /api/api-keys): JWT auth, returns plaintext key exactly once with prominent warning, never stored in DB
- Created ListApiKeysEndpoint (GET /api/api-keys): JWT auth, prefix-only response (no hash, no plaintext)
- Created DeleteApiKeyEndpoint (DELETE /api/api-keys/{keyId}): JWT auth, soft-deactivates
- Created CreateLeadViaApiEndpoint (POST /api/external/leads): AllowAnonymous + X-Api-Key header validation, resolves tenant without JWT via IApiKeyService/ITenantRegistry, creates lead with Source=Api, returns duplicates array per D-16
- Registered IApiKeyService in ConfigureServices and all four endpoints in Endpoints.cs MapLeadsEndpoints()

## Task Commits

Each task was committed atomically:

1. **Task 1: IApiKeyService interface and ApiKeyService implementation** - `0a487ab` (feat)
2. **Task 2: API key management endpoints and CreateLeadViaApiEndpoint** - `8826bb0` (feat)

**Plan metadata:** (docs commit — see final_commit below)

## Files Created/Modified
- `IronMonkey.ApiService/Features/Leads/Ingestion/Api/IApiKeyService.cs` - Interface: GeneratedApiKey record, IApiKeyService, ApiKeyInfo record
- `IronMonkey.ApiService/Features/Leads/Ingestion/Api/ApiKeyService.cs` - BCrypt hash-then-verify, 32-byte random key generation, Scoped via DI
- `IronMonkey.ApiService/Features/Leads/Ingestion/Api/GenerateApiKeyEndpoint.cs` - POST /api/api-keys, JWT RequireAuthorization, warning in response
- `IronMonkey.ApiService/Features/Leads/Ingestion/Api/ListApiKeysEndpoint.cs` - GET /api/api-keys, JWT RequireAuthorization, prefix only
- `IronMonkey.ApiService/Features/Leads/Ingestion/Api/DeleteApiKeyEndpoint.cs` - DELETE /api/api-keys/{keyId}, JWT RequireAuthorization, 204 NoContent
- `IronMonkey.ApiService/Features/Leads/Ingestion/Api/CreateLeadViaApiEndpoint.cs` - POST /api/external/leads, AllowAnonymous, X-Api-Key header, duplicates array
- `IronMonkey.ApiService/ConfigureServices.cs` - Added IApiKeyService scoped registration
- `IronMonkey.ApiService/Endpoints.cs` - Added GenerateApiKeyEndpoint, ListApiKeysEndpoint, DeleteApiKeyEndpoint, CreateLeadViaApiEndpoint registrations

## Decisions Made
- CreateLeadViaApiEndpoint uses `AllowAnonymous` (not `RequireAuthorization`) — external callers have no JWT; tenant context resolved purely from X-Api-Key header
- ITenantRegistry (BackgroundJobs.ITenantRegistry) used instead of ITenantService — ITenantService requires JWT claims context; ITenantRegistry takes explicit tenantId and queries CentralDbContext
- ValidateAsync iterates all active keys with BCrypt.Verify — constant-time comparison prevents timing attacks; prefix-based optimization (to narrow candidates) noted as future improvement in comment

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] CsvImportJob missing ITenantRegistry using directive**
- **Found during:** Task 2 (dotnet build failure)
- **Issue:** `IronMonkey.ApiService.Features.Leads.Ingestion.Csv.CsvImportJob` referenced `ITenantRegistry` without `using IronMonkey.ApiService.BackgroundJobs;` — a parallel plan (03-04) artifact
- **Fix:** Added missing using directive to CsvImportJob.cs
- **Files modified:** `IronMonkey.ApiService/Features/Leads/Ingestion/Csv/CsvImportJob.cs`

**2. [Rule 3 - Blocking] GetWebFormPageEndpoint raw string literal CSS escape error**
- **Found during:** Task 2 (dotnet build failure after CsvImportJob fix)
- **Issue:** `GetWebFormPageEndpoint.cs` used `$"""` raw string literal with `{{ }}` CSS escaping (CS9006 error) — a parallel plan (03-05) artifact; `$$"""` required for double-dollar interpolation
- **Fix:** Changed `$"""` to `$$"""` and updated interpolation holes to `{{expr}}` syntax; replaced CSS double-braces with single braces (not needed in raw strings with `$$`)
- **Files modified:** `IronMonkey.ApiService/Features/Leads/Ingestion/WebForm/GetWebFormPageEndpoint.cs`

## Known Stubs

None — all required functionality is implemented. Test stubs in `ApiLeadCreationTests.cs` and `ApiAuthTests.cs` are Wave 0 stubs pointing to `03-06-PLAN` implementation (as designed).

## User Setup Required

None.

## Next Phase Readiness
- API key management fully functional for 03-06 test implementation
- CreateLeadViaApiEndpoint ready for integration testing in 03-06
- IApiKeyService available as dependency for future rate limiting in 03-06

---
*Phase: 03-lead-ingestion*
*Completed: 2026-03-21*
