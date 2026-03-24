---
phase: 03-lead-ingestion
plan: 04
subsystem: api
tags: [csvimport, hangfire, background-jobs, endpoints, bulk-import, multi-tenant]

# Dependency graph
requires:
  - phase: 03-02
    provides: ImportBatch entity, CsvHelper 33.1.0, Lead.MarkAsPotentialDuplicate()
  - phase: 02-configurable-lead-model
    provides: Lead entity, IDuplicateDetectionService, PipelineStage

provides:
  - CsvImportService: header validation, row parsing, error CSV serialization
  - CsvImportJob: Hangfire background worker with per-row error isolation and duplicate flagging
  - UploadLeadsFromCsvEndpoint: POST /api/leads/import/csv — returns ImportBatchId immediately
  - GetImportStatusEndpoint: GET /api/leads/import/{batchId}/status — polling progress
  - GetImportErrorsEndpoint: GET /api/leads/import/{batchId}/errors — error CSV download

affects:
  - 03-06 (test stubs — CSV import integration tests)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Fail-fast header validation: validate CSV headers in endpoint before enqueueing job"
    - "Per-row error isolation: row parse failures captured, bad rows skipped, good rows imported"
    - "Batch save: save in groups of 100 rows to avoid large transactions"
    - "Temp file lifecycle: endpoint writes to temp file, Hangfire job deletes in finally block"
    - "Duplicate flagging pattern: create all rows per D-14, mark duplicates with MarkAsPotentialDuplicate"

key-files:
  created:
    - IronMonkey.ApiService/Features/Leads/Ingestion/Csv/CsvImportService.cs
    - IronMonkey.ApiService/Features/Leads/Ingestion/Csv/CsvImportJob.cs
    - IronMonkey.ApiService/Features/Leads/Ingestion/Csv/UploadLeadsFromCsvEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Ingestion/Csv/GetImportStatusEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Ingestion/Csv/GetImportErrorsEndpoint.cs
  modified:
    - IronMonkey.ApiService/ConfigureServices.cs
    - IronMonkey.ApiService/Endpoints.cs

key-decisions:
  - "Fail-fast header validation in upload endpoint (not in job) — reject malformed CSV immediately, avoid wasting job queue slot"
  - "Temp file approach for large CSV files — avoids holding memory for background job, job deletes file in finally block"
  - "IDuplicateDetectionService called per-row — creates every lead per D-14, flags matches with IsPotentialDuplicate=true"
  - "Batch save at every 100 rows — prevents large transactions while providing incremental progress updates"
  - "Case-sensitive exact column matching per D-05 — no interactive mapping UI, strict header requirement"

patterns-established:
  - "CSV upload: validate headers synchronously in endpoint, process rows asynchronously in Hangfire job"
  - "Background job cleanup: temp file deleted in finally block regardless of success/failure"

requirements-completed: [INGST-02]

# Metrics
duration: 9min
completed: 2026-03-21
---

# Phase 3 Plan 04: CSV Bulk Import Summary

**CSV import channel: upload endpoint, Hangfire background job with per-row error isolation and duplicate flagging, status polling, and error CSV download**

## Performance

- **Duration:** ~9 min
- **Started:** 2026-03-21T18:28:08Z
- **Completed:** 2026-03-21T18:36:55Z
- **Tasks:** 2
- **Files modified:** 7

## Accomplishments

- Created `CsvImportService` with header validation (strict case-sensitive match per D-05), row-to-Lead parsing, and error CSV serialization
- Created `CsvImportJob` (Hangfire `[Queue("tenant")]`) that processes rows with per-row try/catch, batches DB saves every 100 rows, and cleans up temp file in finally block
- CSV import flags potential duplicates per D-14 using `IDuplicateDetectionService.FindCandidatesAsync` — all rows create leads, matches flagged with `MarkAsPotentialDuplicate`
- `UploadLeadsFromCsvEndpoint` validates CSV headers before enqueueing (fail fast), writes to temp path, creates `ImportBatch`, enqueues `CsvImportJob`, returns `ImportBatchId` immediately
- `GetImportStatusEndpoint` provides polling endpoint with `TotalRows`, `ImportedRows`, `SkippedRows`, `HasErrors`
- `GetImportErrorsEndpoint` deserializes `ErrorDetailsJson` and returns `text/csv` file with `RowNumber,ErrorMessage` columns
- Registered `CsvImportService` as scoped service and registered all 3 endpoints in `MapLeadsEndpoints()`

## Task Commits

1. **Task 1: CsvImportService (header validation and row parsing)** - `1415da8` (feat)
2. **Task 2: CsvImportJob, upload endpoint, status endpoint, error download endpoint** - `3692387` (feat)

**Plan metadata:** (docs commit — see final_commit below)

## Files Created/Modified

- `IronMonkey.ApiService/Features/Leads/Ingestion/Csv/CsvImportService.cs` - Header validation, row parsing, error CSV serialization
- `IronMonkey.ApiService/Features/Leads/Ingestion/Csv/CsvImportJob.cs` - Hangfire worker with per-row isolation, duplicate flagging, batch saves, temp file cleanup
- `IronMonkey.ApiService/Features/Leads/Ingestion/Csv/UploadLeadsFromCsvEndpoint.cs` - POST /api/leads/import/csv
- `IronMonkey.ApiService/Features/Leads/Ingestion/Csv/GetImportStatusEndpoint.cs` - GET /api/leads/import/{batchId}/status
- `IronMonkey.ApiService/Features/Leads/Ingestion/Csv/GetImportErrorsEndpoint.cs` - GET /api/leads/import/{batchId}/errors
- `IronMonkey.ApiService/ConfigureServices.cs` - Added CsvImportService scoped registration
- `IronMonkey.ApiService/Endpoints.cs` - Added 3 CSV import endpoint registrations

## Decisions Made

- Fail-fast header validation in upload endpoint rather than in job — malformed CSV rejected immediately, before temp file write or job enqueue
- Temp file written synchronously during upload, deleted asynchronously in Hangfire job's finally block — handles large files without holding in memory
- Per-row exception handling in job — one bad row never blocks import of remaining rows (D-06)
- Duplicate check per-row in job using existing `IDuplicateDetectionService` — D-14 behavior implemented without special cases

## Deviations from Plan

None — plan executed exactly as written.

**Parallel execution note:** Other parallel plans (03-03, 03-05) modified `ConfigureServices.cs` and `Endpoints.cs` concurrently. My changes were applied on top of their committed state. The pre-existing `CS0542` error in `IronMonkey.Web` (CreatePermission Razor naming conflict) exists before this plan and is documented in the 03-02 SUMMARY — not caused by this plan.

## Known Stubs

None — all CSV import functionality is fully wired. `CsvImportJob` uses real `IDuplicateDetectionService`, real `TenantDbContext`, real `ImportBatch` entity with full lifecycle.

## Self-Check: PASSED

- FOUND: IronMonkey.ApiService/Features/Leads/Ingestion/Csv/CsvImportService.cs
- FOUND: IronMonkey.ApiService/Features/Leads/Ingestion/Csv/CsvImportJob.cs
- FOUND: IronMonkey.ApiService/Features/Leads/Ingestion/Csv/UploadLeadsFromCsvEndpoint.cs
- FOUND: IronMonkey.ApiService/Features/Leads/Ingestion/Csv/GetImportStatusEndpoint.cs
- FOUND: IronMonkey.ApiService/Features/Leads/Ingestion/Csv/GetImportErrorsEndpoint.cs
- FOUND: commit 1415da8 (Task 1)
- FOUND: commit 3692387 (Task 2)
- ApiService build: 0 errors
