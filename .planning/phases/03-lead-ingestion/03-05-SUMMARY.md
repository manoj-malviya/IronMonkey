---
phase: 03-lead-ingestion
plan: 05
subsystem: lead-ingestion
tags: [web-forms, honeypot, bot-detection, lead-capture, public-endpoints]
dependency_graph:
  requires: [03-02]
  provides: [INGST-04]
  affects: [IronMonkey.ApiService.Features.Leads.Ingestion.WebForm]
tech_stack:
  added: []
  patterns:
    - "HTML honeypot field with CSS position:absolute;left:-9999px for bot detection"
    - "ITenantRegistry for connection string resolution without JWT context"
    - "FromForm attribute for HTML form POST binding in Minimal API"
    - "RandomNumberGenerator.Fill + Base64 URL-safe token generation"
key_files:
  created:
    - IronMonkey.ApiService/Features/Leads/Ingestion/WebForm/IWebFormService.cs
    - IronMonkey.ApiService/Features/Leads/Ingestion/WebForm/WebFormService.cs
    - IronMonkey.ApiService/Features/Leads/Ingestion/WebForm/CreateWebFormEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Ingestion/WebForm/GetWebFormPageEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Ingestion/WebForm/SubmitWebFormEndpoint.cs
    - IronMonkey.ApiService/Features/Leads/Ingestion/WebForm/DeleteWebFormEndpoint.cs
  modified:
    - IronMonkey.ApiService/ConfigureServices.cs
    - IronMonkey.ApiService/Endpoints.cs
decisions:
  - "ITenantRegistry used for GetTenantConnectionStringAsync instead of direct Tenant entity access — cleaner abstraction and already registered"
  - "FromForm attribute added to SubmitWebFormEndpoint SubmissionRequest to bind HTML form POST data"
  - "DisableAntiforgery on SubmitWebFormEndpoint since it is a public external endpoint"
metrics:
  duration: "~8 minutes"
  completed: "2026-03-21"
  tasks_completed: 2
  files_created: 6
  files_modified: 2
---

# Phase 3 Plan 5: Web Form Ingestion Channel Summary

**One-liner:** Embeddable web form lead capture with CSS honeypot bot detection, duplicate flagging, and post-submission redirect support.

## What Was Built

INGST-04: Tenant admin can create hosted web forms that external visitors use to submit leads. Forms get a unique URL-safe token. Submissions create leads with `Source=WebForm` and optionally flag duplicates.

### Components

**IWebFormService / WebFormService**
- `CreateAsync` — generates URL-safe token (24 random bytes → Base64 URL-safe, 32 chars) and saves form to central DB
- `GetByTokenAsync` — looks up active form by token; returns null if not found or inactive
- `GetTenantConnectionStringAsync` — delegates to `ITenantRegistry` (no JWT context available for public endpoints)
- `DeleteAsync` — calls `form.Deactivate()` and saves; scoped to tenant ownership
- `ListAsync` — returns all active forms for a tenant with computed hosted URLs

**CreateWebFormEndpoint** (`POST /api/web-forms`)
- Requires JWT authentication
- Accepts: FormName, FieldNames, DefaultPipelineStageId, PostSubmissionRedirectUrl
- Returns: FormId, FormToken, HostedUrl

**GetWebFormPageEndpoint** (`GET /forms/{token}`)
- Anonymous access
- Renders pure HTML form with configured field inputs
- Honeypot: hidden `<input name="Website">` with `position:absolute;left:-9999px;top:-9999px` and `autocomplete="one-time-code"`

**SubmitWebFormEndpoint** (`POST /forms/{token}/submit`)
- Anonymous access, `DisableAntiforgery` (public external endpoint)
- `[FromForm]` binding for HTML form POST data
- Honeypot check first — if `Website` non-empty, silent `400` (bot detected per D-11)
- Token validation — `400` if invalid/inactive
- Creates lead with `LeadSource.WebForm` in tenant DB
- Runs duplicate detection — flags `IsPotentialDuplicate=true` if candidates found (D-15)
- Post-submission: meta-refresh redirect if `PostSubmissionRedirectUrl` set, else thank-you page (D-12)

**DeleteWebFormEndpoint** (`DELETE /api/web-forms/{formId:guid}`)
- Requires JWT authentication
- Calls `webFormService.DeleteAsync` scoped to current tenant

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical functionality] Added `[FromForm]` binding to SubmitWebFormEndpoint**
- **Found during:** Task 2 implementation
- **Issue:** The plan showed `SubmissionRequest request` as a plain parameter. For HTML form POST with `application/x-www-form-urlencoded`, Minimal API needs explicit `[FromForm]` to bind form fields correctly.
- **Fix:** Added `[Microsoft.AspNetCore.Mvc.FromForm]` attribute to the `SubmissionRequest` parameter.
- **Files modified:** SubmitWebFormEndpoint.cs

### Pre-existing Issues (Out of Scope)

- `IronMonkey.Web/Components/SuperAdmin/CreatePermission.razor` has a CS0542 error (member name same as enclosing type). This is a pre-existing issue unrelated to this plan. The `IronMonkey.ApiService` project builds with 0 errors.

## Known Stubs

None — all functionality is fully wired. Token generation uses `RandomNumberGenerator.Fill` (cryptographically secure). Duplicate detection calls real `IDuplicateDetectionService`. Tenant connection string resolved via `ITenantRegistry`.

## Self-Check: PASSED

Files created:
- FOUND: IronMonkey.ApiService/Features/Leads/Ingestion/WebForm/IWebFormService.cs
- FOUND: IronMonkey.ApiService/Features/Leads/Ingestion/WebForm/WebFormService.cs
- FOUND: IronMonkey.ApiService/Features/Leads/Ingestion/WebForm/CreateWebFormEndpoint.cs
- FOUND: IronMonkey.ApiService/Features/Leads/Ingestion/WebForm/GetWebFormPageEndpoint.cs
- FOUND: IronMonkey.ApiService/Features/Leads/Ingestion/WebForm/SubmitWebFormEndpoint.cs
- FOUND: IronMonkey.ApiService/Features/Leads/Ingestion/WebForm/DeleteWebFormEndpoint.cs

Commits:
- FOUND: 8ad062a (feat(03-05): add IWebFormService interface and WebFormService implementation)
- FOUND: 41b570d (feat(03-05): add web form endpoints)
