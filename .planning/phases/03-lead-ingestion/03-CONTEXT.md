# Phase 3: Lead Ingestion - Context

**Gathered:** 2026-03-21
**Status:** Ready for planning

<domain>
## Phase Boundary

Leads can enter the system through any channel — manual entry, bulk CSV import, REST API, or embeddable web form — with consistent validation and source tracking. Each channel uses the existing Lead entity, custom fields, pipeline stages, and duplicate detection from Phase 2. No UI beyond the ingestion flows themselves.

</domain>

<decisions>
## Implementation Decisions

### API Authentication for External Callers
- **D-01:** Tenant API keys for REST API access — tenant admin generates keys, external systems pass via `X-Api-Key` header
- **D-02:** Form tokens for web form submissions — each generated form has a unique token embedded in its URL, submission endpoint validates token maps to real form + tenant, no user auth required
- **D-03:** Per-key rate limits — 100 leads/minute per API key, 10/minute per form token
- **D-04:** Minimal API key management — generate key, copy it, delete it. No naming, expiry, usage stats in v1

### CSV Import Workflow
- **D-05:** Strict column matching — columns must match field names exactly (or use a downloadable template). No interactive mapping UI
- **D-06:** Skip bad rows, import good ones — after import, show report: "85/100 imported, 15 skipped" with downloadable error file listing failures and reasons
- **D-07:** CSV only — no Excel, no Google Sheets in v1
- **D-08:** Background job with polling — "Imports" page lists past and in-progress imports, status updates via polling (every few seconds), shows progress bar and row counts

### Web Form Generation & Embedding
- **D-09:** Pick fields, get a link — tenant selects which lead fields (including custom fields) to include, sets a form name, gets a hosted URL. No visual builder
- **D-10:** Hosted page only — each form gets a unique URL (e.g., `/forms/{token}`). Tenant links to it or iframes it. No JS snippet embed
- **D-11:** Honeypot + rate limiting for spam protection — hidden honeypot field that bots fill, plus per-token rate limit. No CAPTCHA
- **D-12:** Configurable post-submission — tenant chooses: show a thank-you message OR redirect to a URL they specify

### Duplicate Handling During Ingestion
- **D-13:** Manual entry: warning before save — show matched leads with confidence scores. User can create anyway, view existing lead, or merge into existing
- **D-14:** CSV import: import all, flag duplicates — create every row but mark duplicates for review. User handles after import
- **D-15:** Web form: always create, flag for review — visitor doesn't see duplicate info. Lead created but flagged internally with "potential duplicate" badge
- **D-16:** REST API: return duplicate info in response — create the lead, include a `duplicates` array in the response body with matched lead IDs and confidence scores

### Claude's Discretion
- CSV parsing library choice (CsvHelper or similar)
- Import batch entity design and progress tracking schema
- Web form hosted page styling and layout
- Exact rate limiting implementation (middleware vs filter)
- Honeypot field implementation details
- Background job retry and error handling strategy
- Import file storage (temp disk vs blob storage)

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches for each ingestion channel.

</specifics>

<canonical_refs>
## Canonical References

No external specs — requirements are fully captured in decisions above and ROADMAP.md success criteria.

### Upstream phase artifacts
- `.planning/phases/01-multi-tenancy-foundation/01-CONTEXT.md` — Tenant resolution, JWT claims, Hangfire job patterns
- `.planning/ROADMAP.md` §Phase 3 — Success criteria for INGST-01 through INGST-04
- `.planning/REQUIREMENTS.md` §Lead Ingestion — Requirement definitions

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `CreateLeadEndpoint` (POST /api/leads): Already creates leads with source tracking — extend or use as internal service for all channels
- `IDuplicateDetectionService`: Fuzzy matching on email, phone, name with confidence scores — call during every ingestion path
- `ILeadMergeService`: Merge with audit trail — available for manual entry duplicate resolution
- `Lead.Create()` factory method: Static factory with all required fields including `LeadSource` enum
- `LeadSource` enum: Already has `Manual`, `Import`, `Api`, `WebForm` values
- `OutboxProcessingJob`: Hangfire job pattern with tenant queue, retry policy — template for import job
- `TenantRegistry`: Background job tenant resolution — reuse for import job context
- `RequestValidationFilter<T>`: FluentValidation endpoint filter — reuse for new endpoints
- `CustomFieldDefinition` + `CustomFieldValues`: Dynamic field schema — needed for CSV column matching and web form field selection

### Established Patterns
- Tenant DB access: `ITenantService.GetCurrentTenantId()` → `GetConnectionStringAsync()` → `ITenantDbContextFactory.CreateForTenant()`
- Endpoint pattern: `IEndpoint` with static `Map()`, nested Request/Response records, nested `RequestValidator`
- Typed results: `Results<Ok<T>, ValidationError, NotFound, BadRequest>`
- Background jobs: `[Queue("tenant")]`, TenantId as first parameter, resolve connection from `ITenantRegistry`
- Global query filters: TenantId + IsDeleted enforced automatically on TenantDbContext

### Integration Points
- `Endpoints.cs`: Register new ingestion endpoints via `MapEndpoint<T>()`
- `ConfigureServices.cs`: Register new services (ImportService, ApiKeyService, WebFormService)
- `TenantDbContext`: May need new DbSets (ApiKey, WebForm, ImportBatch)
- `IronMonkey.Data/Migrations/Tenant/`: New migration for import/form/apikey entities
- `IronMonkey.Web/`: Blazor components for manual entry form, import page, form builder (if UI work included)

</code_context>

<deferred>
## Deferred Ideas

- Excel (.xlsx) and Google Sheets import support — future enhancement
- JavaScript snippet embed for web forms — future enhancement beyond hosted pages
- API key naming, expiry, and usage analytics — future enhancement
- Interactive column mapping UI for CSV import — future enhancement
- CAPTCHA integration for web forms — add if honeypot proves insufficient
- Webhook callbacks on lead creation via API — consider for Phase 4 workflow engine

</deferred>

---

*Phase: 03-lead-ingestion*
*Context gathered: 2026-03-21*
