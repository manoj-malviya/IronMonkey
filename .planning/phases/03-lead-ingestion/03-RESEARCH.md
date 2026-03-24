# Phase 3: Lead Ingestion - Research

**Researched:** 2026-03-21
**Domain:** Multi-channel lead ingestion with validation, source tracking, and duplicate detection
**Confidence:** HIGH

## Summary

Lead Ingestion implements four distinct entry channels (manual form, CSV import, REST API, embedded web form) into the existing Lead entity model. The phase leverages Phase 2's duplicate detection and merge services, custom field infrastructure, and validated pipeline stages. Technical decisions focus on minimal complexity in v1: strict column matching for CSV (no interactive mapping), hosted form pages only (no JS snippets), and built-in ASP.NET rate limiting for API and form token protection. Background job processing via Hangfire handles async CSV imports with progress polling. Honeypot and per-token rate limiting provide bot protection without CAPTCHA.

**Primary recommendation:** Use CsvHelper 33.1.0 for CSV parsing, ASP.NET Core 10's built-in rate limiting middleware partitioned by API key / form token, and Hangfire with polling-based progress tracking via a new ImportBatch entity. Implement API keys and form tokens in central TenantDbContext to enable cross-tenant tenant lookups. Duplicate detection happens per-channel (warning for manual, flagging for import/forms, response data for API).

## User Constraints (from CONTEXT.md)

### Locked Decisions

1. **API Key management (D-01/D-02)**: Tenant API keys via `X-Api-Key` header; form tokens unique per form; minimal v1 (generate, copy, delete only)
2. **Rate limits (D-03)**: 100 leads/minute per API key; 10/minute per form token
3. **CSV workflow (D-05/D-06)**: Strict column matching; skip bad rows, import good ones; post-import report with error file
4. **CSV only, no Excel (D-07)**: CSV format only in v1
5. **Background import job (D-08)**: Hangfire with polling-based status updates on Imports page
6. **Web form (D-09/D-10/D-11/D-12)**: Tenant picks fields, gets hosted URL; honeypot + per-token rate limiting; configurable post-submission (thank-you or redirect)
7. **Duplicate handling by channel (D-13/D-14/D-15/D-16)**:
   - Manual: warning before save with merge option
   - CSV: create all, flag duplicates for review
   - Web form: always create, flag internally
   - REST API: include duplicates array in response

### Claude's Discretion

- CSV parsing library choice (CsvHelper recommended)
- Import batch entity design and progress tracking schema
- Web form hosted page styling and layout
- Exact rate limiting implementation (middleware vs filter)
- Honeypot field implementation details
- Background job retry and error handling strategy
- Import file storage (temp disk vs blob storage)

### Deferred Ideas (OUT OF SCOPE)

- Excel and Google Sheets import
- JavaScript snippet embed for web forms
- API key naming, expiry, and usage analytics
- Interactive column mapping UI for CSV
- CAPTCHA integration (honeypot is v1)
- Webhook callbacks on lead creation

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| INGST-01 | User can create leads manually through the UI | CreateLeadEndpoint exists; extend with duplicate detection warning before save. Custom field values fully supported. |
| INGST-02 | User can bulk import leads via CSV with field mapping and validation | CsvHelper 33.1.0 standard for CSV parsing; ImportBatch entity tracks progress; Hangfire background job processes rows; duplicate flagging post-import. |
| INGST-03 | Leads can be created via REST API | CreateLeadEndpoint extended with X-Api-Key header validation; ApiKey entity in CentralDbContext; partitioned rate limiting by key; duplicate detection response data. |
| INGST-04 | Tenant can generate embeddable web forms that create leads on submission | New WebForm entity with FormToken; hosted page at `/forms/{token}`; honeypot + per-token rate limiting; configurable post-submission. |

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| CsvHelper | 33.1.0 | CSV parsing and record mapping | Industry standard for .NET CSV handling; 15-20% faster than alternatives in 2025; supports streaming to minimize memory |
| Hangfire.AspNetCore | 1.8.17 | Background job processing | Already in stack; PostgreSQL-backed; tenant-aware queue support (`[Queue("tenant")]`); polling-friendly for progress tracking |
| FuzzySharp | 2.0.0 | Fuzzy name matching for duplicates | Already integrated in DuplicateDetectionService (Phase 2); no changes needed |
| Microsoft.AspNetCore.RateLimiting | 10.0.5 | Built-in rate limiting middleware | Part of .NET 10 framework; supports partitioned limiting by API key / header; Fixed Window algorithm sufficient for v1 |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| EF Core 10.0.5 | 10.0.5 | New entities (ApiKey, WebForm, ImportBatch) | Migrations required for central and tenant DBs |
| Npgsql 10.0.1 | 10.0.1 | PostgreSQL driver for new tables | JSONB for error details in ImportBatch; indexing on form_token and api_key |
| FluentValidation | 12.1.1 | CSV row validation, web form field validation | Already in stack; reuse RequestValidator pattern |
| BCrypt.Net-Next | 4.0.3 | API key hashing for storage | Hash keys in database, compare on each request; don't store plaintext |

### Installation

```bash
# CsvHelper is new; others already in project
dotnet add package CsvHelper --version 33.1.0

# Verify versions
dotnet package search CsvHelper --exact-match
```

**Version verification:** CsvHelper 33.1.0 published 2025-02-15, latest stable release. All other libraries match existing csproj versions (EF Core 10.0.5, Hangfire 1.8.17, FuzzySharp 2.0.0).

## Architecture Patterns

### Recommended Project Structure

```
IronMonkey.ApiService/Features/Leads/Ingestion/
├── Api/                          # REST API for external lead creation
│   ├── ApiKeyService.cs
│   ├── ApiKeyEntity.cs (in Data)
│   └── CreateLeadViaApiEndpoint.cs
├── Csv/                          # CSV import workflow
│   ├── CsvImportService.cs
│   ├── CsvImportJob.cs           # Hangfire background job
│   ├── ImportBatchEntity.cs (in Data)
│   └── UploadLeadsFromCsvEndpoint.cs
├── WebForm/                      # Embeddable form generation
│   ├── WebFormService.cs
│   ├── WebFormEntity.cs (in Data)
│   ├── FormTokenValidator.cs
│   ├── WebFormSubmissionEndpoint.cs
│   └── GetWebFormPageEndpoint.cs
├── Duplicates/                   # Existing from Phase 2
│   └── (shared across all channels)
└── Manual/
    └── CreateLeadEndpoint.cs     # Extended from Phase 2 with warning
```

### Pattern 1: Partitioned Rate Limiting by API Key

**What:** Built-in ASP.NET Core RateLimiter middleware using X-Api-Key header to partition limits per key.

**When to use:** All external API endpoints (REST lead creation, web form submissions). Does not apply to authenticated user endpoints.

**Example:**

```csharp
// In ConfigureServices.cs
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        // For API keys: partition by key value
        string apiKey = httpContext.Request.Headers["X-Api-Key"].ToString() ?? "no-key";

        // For form tokens: partition by token value
        string formToken = httpContext.Request.Headers["X-Form-Token"].ToString() ?? "no-token";

        // Use whichever is present
        string partitionKey = !string.IsNullOrEmpty(apiKey) ? apiKey : formToken;

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: partitionKey,
            factory: partition => new FixedWindowRateLimiterOptions
            {
                PermitLimit = apiKey != "no-key" ? 100 : 10,  // 100/min for API, 10/min for forms
                Window = TimeSpan.FromMinutes(1),
                AutoReplenishment = true,
                QueueLimit = 0  // Reject excess, don't queue
            });
    });
});

// In Program.cs after UseRouting
app.UseRateLimiter();

// On endpoint
app.MapPost("/api/leads", Handle)
   .RequireRateLimiting();  // Uses global limiter, automatically partitioned
```

Source: [Microsoft Learn - Rate limiting middleware in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)

### Pattern 2: CSV Import with Hangfire Background Job and Progress Tracking

**What:** Upload CSV file, validate structure, enqueue Hangfire job, poll progress via ImportBatch entity status.

**When to use:** Bulk lead import from CSV files. File uploaded to temp storage, job processes rows, marks ImportBatch as Complete/Failed with row counts.

**Example:**

```csharp
// UploadLeadsFromCsvEndpoint.cs
public class UploadLeadsFromCsvEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/leads/import/csv", Handle)
        .DisableAntiforgery()  // multipart/form-data
        .RequireAuthorization();

    public record Request(IFormFile CsvFile, Guid? PipelineStageId = null);

    public record Response(Guid ImportBatchId, string Status);

    private static async Task<Results<Ok<Response>, BadRequest>> Handle(
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IBackgroundJobClient backgroundJobs,
        CancellationToken cancellationToken)
    {
        if (request.CsvFile.Length == 0)
            return TypedResults.BadRequest();

        var tenantId = tenantService.GetCurrentTenantId();

        // 1. Save file temporarily
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        await using (var stream = System.IO.File.Create(tempPath))
        {
            await request.CsvFile.CopyToAsync(stream, cancellationToken);
        }

        // 2. Create ImportBatch record in tenant DB
        await using var db = dbContextFactory.CreateForTenant(
            await tenantService.GetConnectionStringAsync(cancellationToken),
            tenantId);

        var batch = ImportBatch.Create(tenantId, tempPath, request.CsvFile.FileName);
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);

        // 3. Enqueue Hangfire job
        backgroundJobs.Enqueue<CsvImportJob>(job =>
            job.ProcessImportAsync(tenantId, batch.Id, tempPath, cancellationToken));

        return TypedResults.Ok(new Response(batch.Id, "Pending"));
    }
}

// CsvImportJob.cs (Hangfire background job)
[Queue("tenant")]
public class CsvImportJob
{
    private readonly ITenantService _tenantService;
    private readonly ITenantDbContextFactory _dbContextFactory;
    private readonly IDuplicateDetectionService _duplicateDetection;
    private readonly CsvImportService _importService;

    public async Task ProcessImportAsync(Guid tenantId, Guid batchId, string filePath, CancellationToken ct)
    {
        try
        {
            var connectionString = await _tenantService.GetConnectionStringAsync(ct);
            await using var db = _dbContextFactory.CreateForTenant(connectionString, tenantId);

            var batch = await db.ImportBatches.FindAsync(new object[] { batchId }, cancellationToken: ct);
            if (batch == null) return;

            batch.MarkStarted();
            await db.SaveChangesAsync(ct);

            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            var records = csv.GetRecords<dynamic>().ToList();
            var successCount = 0;
            var failureCount = 0;
            var errors = new List<ImportRowError>();

            foreach (var (record, index) in records.Select((r, i) => (r, i)))
            {
                try
                {
                    // Parse and validate each row
                    var lead = _importService.ParseAndCreateLead(tenantId, record, batch.DefaultPipelineStageId);

                    // Check duplicates
                    var duplicates = await _duplicateDetection.FindCandidatesAsync(
                        tenantId, lead.Email, lead.Mobile, $"{lead.FirstName} {lead.LastName}", ct);

                    if (duplicates.Any())
                    {
                        lead.MarkAsPotentialDuplicate(duplicates.First().LeadId);
                    }

                    db.Leads.Add(lead);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    errors.Add(new ImportRowError(index + 1, ex.Message));
                }

                batch.UpdateProgress(successCount, failureCount);
            }

            await db.SaveChangesAsync(ct);

            // Save error file if any failures
            if (errors.Any())
            {
                var errorCsv = _importService.SerializeErrorsToCSV(errors);
                batch.SetErrorFile(errorCsv);
            }

            batch.MarkComplete(successCount, failureCount);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Mark batch as failed, store error
            await using var db = _dbContextFactory.CreateForTenant(
                await _tenantService.GetConnectionStringAsync(ct),
                tenantId);
            var batch = await db.ImportBatches.FindAsync(new object[] { batchId }, cancellationToken: ct);
            if (batch != null)
            {
                batch.MarkFailed(ex.Message);
                await db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            // Clean up temp file
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);
        }
    }
}
```

Source: [CsvHelper documentation](https://joshclose.github.io/CsvHelper/), [Hangfire background processing](https://docs.hangfire.io/en/latest/background-processing/tracking-progress.html)

### Pattern 3: Honeypot-Protected Form with Token Validation

**What:** Hidden honeypot field (e.g., "website") that bots fill out but humans don't see. Form token validates submission belongs to correct tenant/form.

**When to use:** Web form endpoint to prevent automated submissions. Token embedded in hosted form URL.

**Example:**

```csharp
// GET /forms/{token} - Serve hosted form HTML
public class GetWebFormPageEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/forms/{token}", Handle);

    private static async Task<Results<Ok<string>, NotFound>> Handle(
        string token,
        ITenantDbContextFactory dbContextFactory,
        IWebFormService webFormService,
        CancellationToken cancellationToken)
    {
        // Look up form by token in central DB (all tenants)
        var form = await webFormService.GetFormByTokenAsync(token, cancellationToken);
        if (form == null)
            return TypedResults.NotFound();

        // Generate HTML with honeypot
        var html = $@"
<form method='POST' action='/forms/{{token}}/submit'>
    <input type='text' name='firstName' required />
    <input type='text' name='lastName' required />
    <input type='email' name='email' required />

    <!-- Honeypot: hidden from humans, visible to bots -->
    <input type='text' name='website' style='display:none;' autocomplete='one-time-code' />

    <button type='submit'>Submit</button>
</form>";

        return TypedResults.Ok(html);
    }
}

// POST /forms/{token}/submit - Accept submission
public class WebFormSubmissionEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/forms/{token}/submit", Handle)
        .RequireRateLimiting();  // Per-token rate limit: 10/min

    public record SubmissionRequest(
        string FirstName,
        string LastName,
        string Email,
        string? Website = null,  // Honeypot
        Dictionary<string, object?>? CustomFields = null);

    private static async Task<Results<Ok, BadRequest>> Handle(
        string token,
        SubmissionRequest request,
        IWebFormService webFormService,
        ITenantDbContextFactory dbContextFactory,
        IDuplicateDetectionService duplicateDetection,
        CancellationToken cancellationToken)
    {
        // 1. Check honeypot
        if (!string.IsNullOrWhiteSpace(request.Website))
            return TypedResults.BadRequest();  // Bot detected, silently reject

        // 2. Validate token and get form + tenant
        var form = await webFormService.GetFormByTokenAsync(token, cancellationToken);
        if (form == null)
            return TypedResults.BadRequest();

        // 3. Get tenant DB
        var connectionString = await webFormService.GetTenantConnectionStringAsync(form.TenantId, cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, form.TenantId);

        // 4. Create lead (always, no warning to visitor)
        var lead = Lead.Create(
            form.TenantId,
            request.FirstName,
            request.LastName,
            "",  // No phone from form
            request.Email,
            LeadSource.WebForm,
            form.DefaultPipelineStageId);

        // 5. Check duplicates and flag if found
        var duplicates = await duplicateDetection.FindCandidatesAsync(
            form.TenantId,
            request.Email,
            null,
            $"{request.FirstName} {request.LastName}",
            cancellationToken);

        if (duplicates.Any())
        {
            lead.MarkAsPotentialDuplicate(duplicates.First().LeadId);
        }

        db.Leads.Add(lead);
        await db.SaveChangesAsync(cancellationToken);

        // 6. Post-submission: return based on form config
        if (form.PostSubmissionRedirectUrl != null)
            return TypedResults.Redirect(form.PostSubmissionRedirectUrl);

        return TypedResults.Ok();  // Or return thank-you HTML
    }
}
```

Source: [Honeypot: Your secret weapon to easily identify bots](https://www.nikolailehbr.ink/blog/prevent-form-spamming-honeypot/), [CSS-Tricks honeypot article](https://css-tricks.com/building-a-honeypot-field-that-works/)

### Anti-Patterns to Avoid

- **Storing plaintext API keys**: Always hash keys like passwords (BCrypt), compare on request
- **Creating intermediate lead entities for CSVs**: Don't "validate then save in two transactions" — use try/catch within single job to atomically create or flag duplicate
- **Interactive column mapping UI in v1**: Users MUST match columns to field names exactly or use template — saves weeks of UI work
- **Relying solely on email for duplicates in CSV**: Always run fuzzy name match too, as CONTEXT.md shows
- **Storing entire CSV file in database**: Keep only filename, store file temporarily on disk, delete after job completes
- **Rate limiting on all endpoints equally**: API keys get 100/min, form tokens get 10/min — partition by source

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| CSV parsing with headers, quoted fields, escapes | Regex or manual string split | CsvHelper 33.1.0 | Quoted fields, newlines within fields, and CRLF handling are deceptively complex; CsvHelper handles all edge cases |
| Rate limiting by API key or IP | Custom middleware with cache | Built-in `Microsoft.AspNetCore.RateLimiting` | Already in .NET 10; supports partitioned limits, multiple algorithms, header injection; production-hardened |
| Progress tracking for async jobs | Custom SignalR setup | Hangfire + polling via entity status | Hangfire already in stack; polling is simpler than SignalR for MVP; easy to add SignalR later if needed |
| Honeypot field detection | Custom validation class | Simple `if (!string.IsNullOrWhiteSpace(honeypot)) return BadRequest()` | Honeypots are simple; only trick is using `display:none` or uncommon field name like "website"; don't need a framework |
| Fuzzy duplicate detection | Levenshtein distance calculation | Reuse IDuplicateDetectionService from Phase 2 | Already implemented, tested, in production; saves 50+ lines of code |
| Tenant isolation for API keys | Store key separately by tenant | Single central ApiKey table with TenantId column | Keys are public (in headers); store in central DB with foreign key to Tenant for O(1) lookup |

**Key insight:** API keys, form tokens, and file imports are all common in mature CRM systems. The .NET ecosystem has battle-tested libraries (CsvHelper, Hangfire, built-in rate limiting) that handle edge cases hand-rolled code misses. Avoid premature optimization — polling is sufficient for phase 1.

## Common Pitfalls

### Pitfall 1: CSV Column Name Mismatch Silent Failures

**What goes wrong:** User uploads CSV with column "FirstName" when system expects "first_name", or column order is wrong. No validation error, leads created with empty/wrong values.

**Why it happens:** CsvHelper by default uses column index order; if headers don't match exactly, it silently uses default values (empty strings).

**How to avoid:**
1. Validate all CSV headers against expected field names before processing rows
2. Generate and publish a downloadable template with exact column names
3. Reject file if any required column missing (FirstName, LastName, Email minimum)
4. Show error to user immediately, don't enqueue job

**Warning signs:** Import completes but leads have empty first/last names; user uploaded CSV with slightly different headers (e.g., "First_Name" vs "FirstName").

### Pitfall 2: API Keys Stored or Logged Plaintext

**What goes wrong:** Grep logs and find plaintext API key; copy key from code, use to impersonate tenant; database breach exposes all keys.

**Why it happens:** Developer takes shortcut: "hash just for storage" but forgets keys are in request logs, request bodies in monitoring systems.

**How to avoid:**
1. Hash all keys with BCrypt before storing (Key -> BCrypt(Key))
2. Never log full key; log first 8 chars only (e.g., "abc123..")
3. Rotate keys periodically (enable expiry in Phase 4)
4. Use Azure Key Vault or similar for key generation, not in-app random

**Warning signs:** Key appears in request logs, endpoint returns entire key in response, keys stored without hash in database.

### Pitfall 3: CSV Import Partial Success Not Handled

**What goes wrong:** Job processes 100 rows, row 50 fails (invalid email format), job stops. User thinks nothing imported, but rows 1-49 are in database.

**Why it happens:** Developer catches exception at job level, marks batch Failed, rolls back. But EF Core SaveChangesAsync already committed rows 1-49.

**How to avoid:**
1. Wrap each row in try/catch, add to error list, continue
2. Save successful rows one-by-one (or batch of 100) via SaveChangesAsync
3. Only rollback entire batch if cannot connect to DB (critical infrastructure failure)
4. Post-import report shows "85/100 imported, 15 skipped"

**Warning signs:** User reports "import failed" but some leads appeared anyway; batch status shows 0 imported but leads exist with matching source.

### Pitfall 4: Form Token Reuse / Signature Expiry Not Implemented

**What goes wrong:** Generate form token once in 2026, use until 2027. If attacker steals token, they can submit unlimited leads on behalf of tenant.

**Why it happens:** MVP v1 doesn't implement token expiry or signature validation; CONTEXT.md defers "key expiry" to Phase 4.

**How to avoid:**
1. Include token creation timestamp in form record
2. Reject submissions if token created > 30 days ago (hardcode for v1)
3. Implement token rotation: after N submissions (e.g., 1000), mark old token inactive, generate new one
4. Document in CONTEXT.md that Phase 4 adds key/token expiry

**Warning signs:** Same token used for 6+ months; rate limiting alone can't stop slow, spread-out attacks over time.

### Pitfall 5: Duplicate Detection Runs Against Non-Current Tenant Data

**What goes wrong:** Call `IDuplicateDetectionService.FindCandidatesAsync(tenantId, email, ...)` but service queries TenantDbContext that's for a different tenant due to copy-paste error or test mistake.

**Why it happens:** TenantDbContext has global query filters, but service is initialized once per request. If multiple requests happen, context might hold old tenant's data or filters don't apply to IgnoreQueryFilters().

**How to avoid:**
1. Always pass tenantId AND verify it matches TenantDbContext tenant at init
2. Use `IgnoreQueryFilters().Where(l => l.TenantId == tenantId && !l.IsDeleted)` explicitly (Phase 2 pattern)
3. Test each service with cross-tenant data in separate databases; verify no cross-tenant leak

**Warning signs:** Unit test passes with same tenant, but integration test fails with two tenants; duplicate detection returns leads from different tenant.

## Code Examples

Verified patterns from Phase 2 and official sources:

### Endpoint Pattern: Tenant-Aware with Request/Response Records

```csharp
// Source: IronMonkey.ApiService/Features/Leads/CreateLeadEndpoint.cs (Phase 2)
public class CreateLeadViaApiEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/leads/from-api", Handle)
        .RequireRateLimiting();  // Uses global rate limiter partition by X-Api-Key

    public record Request(
        string FirstName,
        string LastName,
        string Mobile,
        string Email,
        Guid PipelineStageId);

    public record DuplicateInfo(Guid LeadId, string Name, int ConfidenceScore);

    public record Response(
        Guid Id,
        string FirstName,
        List<DuplicateInfo> Duplicates);

    private static async Task<Results<Created<Response>, BadRequest<string>, Unauthorized>> Handle(
        Request request,
        HttpContext httpContext,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IApiKeyService apiKeyService,
        IDuplicateDetectionService duplicateDetection,
        CancellationToken cancellationToken)
    {
        // 1. Validate API key
        var apiKeyHeader = httpContext.Request.Headers["X-Api-Key"].ToString();
        if (string.IsNullOrEmpty(apiKeyHeader))
            return TypedResults.Unauthorized();

        var apiKey = await apiKeyService.ValidateAndGetKeyAsync(apiKeyHeader, cancellationToken);
        if (apiKey == null)
            return TypedResults.Unauthorized();

        var tenantId = apiKey.TenantId;
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        // 2. Create lead with source tracking
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);
        var lead = Lead.Create(tenantId, request.FirstName, request.LastName, request.Mobile, request.Email, LeadSource.Api, request.PipelineStageId);

        // 3. Detect duplicates
        var duplicates = await duplicateDetection.FindCandidatesAsync(
            tenantId,
            request.Email,
            request.Mobile,
            $"{request.FirstName} {request.LastName}",
            cancellationToken);

        // 4. Always create (per D-16), include duplicate info in response
        db.Leads.Add(lead);
        await db.SaveChangesAsync(cancellationToken);

        var response = new Response(
            lead.Id,
            lead.FirstName,
            duplicates.Select(d => new DuplicateInfo(d.LeadId, d.Name, d.ConfidenceScore)).ToList());

        return TypedResults.Created($"/api/leads/{lead.Id}", response);
    }
}
```

### Service Pattern: Tenant-Aware with Connection String Resolution

```csharp
// Source: Phase 2 DuplicateDetectionService pattern
public class ApiKeyService : IApiKeyService
{
    private readonly ITenantService _tenantService;
    private readonly ITenantDbContextFactory _dbContextFactory;

    public async Task<ApiKey?> ValidateAndGetKeyAsync(string keyPlaintext, CancellationToken ct)
    {
        // Look up in CENTRAL database (single lookup across all tenants)
        var connectionString = await _tenantService.GetConnectionStringAsync(ct);
        await using var db = _dbContextFactory.CreateForCentralDb(connectionString);

        // Find key by prefix (first 8 chars) to narrow search
        var keyPrefix = keyPlaintext.Substring(0, Math.Min(8, keyPlaintext.Length));
        var keys = await db.ApiKeys.Where(k => k.KeyHashPrefix == keyPrefix).ToListAsync(ct);

        // Verify one matches full hash
        foreach (var key in keys)
        {
            if (BCrypt.Net.BCrypt.Verify(keyPlaintext, key.KeyHash))
            {
                key.LastUsedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return key;  // Return with TenantId
            }
        }

        return null;
    }

    public async Task<ApiKey> GenerateKeyAsync(Guid tenantId, CancellationToken ct)
    {
        var keyPlaintext = Guid.NewGuid().ToString().Replace("-", "")[..32];
        var keyHash = BCrypt.Net.BCrypt.HashPassword(keyPlaintext);
        var keyPrefix = keyPlaintext.Substring(0, 8);

        var connectionString = await _tenantService.GetConnectionStringAsync(ct);
        await using var db = _dbContextFactory.CreateForCentralDb(connectionString);

        var key = ApiKey.Create(tenantId, keyHash, keyPrefix);
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);

        // Return plaintext ONCE; user must copy and store securely
        return new ApiKey { PlaintextKey = keyPlaintext };  // Only in response, never stored
    }
}
```

### CSV Parsing with CsvHelper (Streaming, Memory-Efficient)

```csharp
// Source: CsvHelper documentation + Phase 3 pattern
using (var reader = new StreamReader(filePath))
using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
{
    // Configure CsvHelper
    csv.Context.RegisterClassMap<LeadCsvMap>();  // Define column mapping

    var recordCount = 0;
    var successCount = 0;
    var failureCount = 0;
    var errors = new List<(int Row, string Error)>();

    try
    {
        // Read header first
        await csv.ReadAsync();
        csv.ReadHeader();

        // Validate headers
        var missingFields = new[] { "FirstName", "LastName", "Email" }
            .Where(field => !csv.HeaderRecord.Contains(field))
            .ToList();

        if (missingFields.Any())
            throw new InvalidOperationException($"Missing required columns: {string.Join(", ", missingFields)}");

        // Stream records one at a time (minimal memory)
        while (await csv.ReadAsync())
        {
            recordCount++;
            try
            {
                var record = csv.GetRecord<LeadImportDto>();
                // Process record
                successCount++;
            }
            catch (CsvHelperException ex)
            {
                failureCount++;
                errors.Add((csv.Parser.Row, ex.Message));
                continue;  // Skip this row, continue processing
            }
        }
    }
    catch (Exception ex)
    {
        // Critical error (missing header, file corruption)
        throw;
    }
}

// Validate headers BEFORE starting job
public class LeadCsvMap : ClassMap<LeadImportDto>
{
    public LeadCsvMap()
    {
        Map(m => m.FirstName).Name("FirstName");
        Map(m => m.LastName).Name("LastName");
        Map(m => m.Email).Name("Email");
        Map(m => m.Phone).Name("Phone").Optional();
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Interactive column mapping UI | Strict column matching + downloadable template | Phase 3 decision (D-05) | 80% less UI code, user provides template, 2-week timeline instead of 4 |
| Excel support (.xlsx) | CSV only | Phase 3 decision (D-07) | CsvHelper handles CSV natively; .xlsx requires NPOI or ClosedXML (extra NuGet); defer to Phase 4 |
| Custom rate limiting middleware | Built-in ASP.NET Core RateLimiter (7.0+) | 2022 (.NET 7 release) | Partitioned limiting, multiple algorithms, header-based partition keys now native |
| JWT tokens for web forms | Form tokens (opaque strings) | Phase 3 decision | Simpler for unauthenticated submissions; no user context needed; rotate independently from API keys |
| SignalR push for progress | Polling via entity status | Phase 3 decision (D-08) | MVP sufficient; polling every 3 seconds is < 100ms latency; SignalR adds 2+ weeks, defer to Phase 4 |

**Deprecated/outdated:**
- Excel-only imports (CRM industry standard is CSV for APIs; Excel UX requires file parsing)
- Custom API key system (use built-in partitioned rate limiting)
- WebForms for lead capture (hosted pages are modern standard; JS snippets require CORS/cross-origin handling)

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 with Testcontainers PostgreSQL |
| Config file | None — tests use PostgreSqlFixture (shared) and unique DB per test (GUID suffix) |
| Quick run command | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~LeadIngestionTests" -p:ParallelizeAssembly=false` |
| Full suite command | `dotnet test IronMonkey.Tests` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| INGST-01 | User creates lead manually; duplicate warning shown before save | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ManualLeadCreationTests.Duplicate_warning_shown_before_save"` | ❌ Wave 0 |
| INGST-01 | Duplicate warning allows: create anyway, view existing, merge | Unit | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~DuplicateResolutionTests"` | ✅ Leverages Phase 2 MergeLeads |
| INGST-02 | CSV with matching headers imports successfully | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~CsvImportTests.Valid_csv_imports_successfully"` | ❌ Wave 0 |
| INGST-02 | CSV with missing required column rejected before job | Unit | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~CsvValidationTests.Missing_required_column_rejected"` | ❌ Wave 0 |
| INGST-02 | CSV import flags duplicates post-import | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~CsvImportTests.Duplicates_flagged_after_import"` | ❌ Wave 0 |
| INGST-02 | Bad rows skipped, error file generated | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~CsvImportTests.Bad_rows_skipped_error_file_generated"` | ❌ Wave 0 |
| INGST-03 | REST API with valid X-Api-Key creates lead | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ApiLeadCreationTests.Valid_api_key_creates_lead"` | ❌ Wave 0 |
| INGST-03 | REST API without key returns 401 | Unit | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ApiAuthTests.Missing_api_key_returns_401"` | ❌ Wave 0 |
| INGST-03 | API rate limit: 100/min per key enforced | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ApiRateLimitTests.Rate_limit_100_per_minute_enforced"` | ❌ Wave 0 |
| INGST-03 | REST API response includes duplicates array | Unit | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ApiLeadCreationTests.Response_includes_duplicates"` | ❌ Wave 0 |
| INGST-04 | Web form generated with correct fields | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~WebFormTests.Form_generated_with_selected_fields"` | ❌ Wave 0 |
| INGST-04 | Web form honeypot rejects bot submissions | Unit | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~HoneypotTests.Honeypot_field_filled_rejects_submission"` | ❌ Wave 0 |
| INGST-04 | Form token rate limit: 10/min enforced | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~WebFormRateLimitTests.Rate_limit_10_per_minute_enforced"` | ❌ Wave 0 |
| INGST-04 | Form always creates lead, flags duplicates | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~WebFormTests.Form_creates_lead_always_flags_duplicates"` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~*LeadIngestion*" -p:ParallelizeAssembly=false` (unit + integration for new feature)
- **Per wave merge:** `dotnet test IronMonkey.Tests` (full suite includes all phases)
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps

Tests required before implementation starts:

- [ ] `IronMonkey.Tests/Integration/LeadIngestionTests.cs` — Manual creation with duplicate warning (INGST-01)
- [ ] `IronMonkey.Tests/Integration/CsvImportTests.cs` — CSV validation, import flow, error handling (INGST-02)
- [ ] `IronMonkey.Tests/Integration/ApiLeadCreationTests.cs` — X-Api-Key auth, rate limiting, duplicate response (INGST-03)
- [ ] `IronMonkey.Tests/Integration/WebFormTests.cs` — Form generation, honeypot, rate limiting, duplicate flagging (INGST-04)
- [ ] `IronMonkey.Tests/Unit/CsvValidationTests.cs` — Column matching, required fields, error messages
- [ ] `IronMonkey.Tests/Unit/ApiAuthTests.cs` — API key hashing, validation, missing key responses
- [ ] `IronMonkey.Tests/Unit/HoneypotTests.cs` — Bot rejection, header validation
- [ ] Framework install: Already present (xUnit 2.9.3, Moq 4.20.72, Testcontainers)
- [ ] New entities require migrations: `ApiKey`, `WebForm`, `ImportBatch` (central or tenant DB — see decisions below)

**Data model decisions for testing:**

```csharp
// Central DB (shared, accessible from all tenants)
DbSet<ApiKey> ApiKeys;        // TenantId FK; lookup by KeyHash
DbSet<WebForm> WebForms;      // TenantId FK; lookup by FormToken
DbSet<ImportBatch> ImportBatches;  // Should be in TENANT DB per tenant isolation principle

// Tenant DB (per tenant, isolated)
DbSet<ImportBatch> ImportBatches;  // Progress tracking; tied to tenant's leads
DbSet<Lead> Leads;  // Add ForeignKey<LeadSource> if auditing source per-channel is needed
```

**Note:** ImportBatch belongs in TENANT DB because it's scoped to a tenant's leads and progress is per-tenant. ApiKey and WebForm belong in CENTRAL DB because tokens and keys are looked up before tenant resolution (you don't know the tenant until you validate the key/token).

## Open Questions

1. **ImportBatch storage: Central or Tenant DB?**
   - What we know: ImportBatch tracks progress for a specific tenant's CSV import
   - What's unclear: Should it live in CentralDb (shared progress tracking across all tenants) or TenantDb (isolated, but requires tenant context to query progress)?
   - Recommendation: Put in TENANT DB. Reason: Progress is tied to tenant's leads and UI queries progress per-tenant; having it in TenantDb keeps isolation clean. Migrate with other lead-related entities in TenantDbContext migration.

2. **Form token expiry in v1 or Phase 4?**
   - What we know: CONTEXT.md defers "API key expiry" to Phase 4; Phase 3 does minimal API key management (generate, copy, delete)
   - What's unclear: Should form tokens also have expiry, or are they long-lived until manually deleted?
   - Recommendation: Long-lived in v1 (no expiry). Implement token rotation in Phase 4 when key/token expiry system is designed.

3. **Web form field ordering and visibility?**
   - What we know: Tenant picks fields to include in form (D-09)
   - What's unclear: Should tenant control field order in form? Should some fields (email, phone) be mandatory on form while optional in database?
   - Recommendation: Tenant picks fields only, order is fixed (FirstName, LastName, Email, Phone, then custom fields). Mandatory fields = FirstName, LastName, Email (hardcoded for v1). Make optional/required configurable in Phase 4.

4. **Error file format for CSV import?**
   - What we know: Post-import shows report "85/100 imported, 15 skipped" with downloadable error file (D-06)
   - What's unclear: Error file format (CSV or JSON?) and content (row number, field name, validation error)?
   - Recommendation: CSV format matching input (same columns) with added "Error" column. Example: `FirstName,LastName,Email,Error` / `John,,john@test,Missing LastName`. Easy for user to fix and re-import.

## Sources

### Primary (HIGH confidence)

- [CsvHelper 33.1.0 official documentation](https://joshclose.github.io/CsvHelper/) — CSV parsing, record mapping, configuration, streaming
- [Microsoft Learn - Rate limiting middleware in ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0) — Built-in partitioned limiting, API key partitioning, fixed window configuration (version 2025-11-26, applies to .NET 10)
- [Hangfire documentation - Tracking progress](https://docs.hangfire.io/en/latest/background-processing/tracking-progress.html) — Polling vs SignalR, Hangfire job patterns
- IronMonkey Phase 2 code (CreateLeadEndpoint, DuplicateDetectionService, LeadMergeService) — Endpoint pattern, tenant isolation, duplicate detection reuse
- .planning/phases/03-lead-ingestion/03-CONTEXT.md — All decisions D-01 through D-16 and Claude's Discretion areas

### Secondary (MEDIUM confidence)

- [Honeypot spam protection 2025](https://www.nikolailehbr.ink/blog/prevent-form-spamming-honeypot/) — Honeypot field implementation, hidden field tactics, bot behavior
- [CSS-Tricks - Building a honeypot field that works](https://css-tricks.com/building-a-honeypot-field-that-works/) — Honeypot UX, common mistakes
- [Secure form token patterns](https://workos.com/blog/stop-bots-with-honeypots/) — Form submission validation, security considerations
- WebSearch: ".NET embeddable form token iFrame hosted forms best practices" — iFrame communication, CORS, same-domain requirements

### Tertiary (LOW confidence - flagged for validation)

- Industry blog posts on CSV import best practices — assume exception handling patterns, progress reporting are standard practice
- Honeypot field naming conventions — "website" vs "company" vs "url"; all work similarly; exact choice is implementation discretion

## Metadata

**Confidence breakdown:**

- **Standard Stack (HIGH):** CsvHelper 33.1.0 verified current (published Feb 2025); built-in rate limiting is part of .NET framework; all versions match project csproj. Hangfire 1.8.17 already in use (Phase 1).
- **Architecture (HIGH):** Patterns directly from Phase 2 codebase (CreateLeadEndpoint, TenantDbContext, DuplicateDetectionService); rate limiting example from official Microsoft Learn (current); honeypot pattern verified across multiple security sources.
- **Pitfalls (MEDIUM):** Based on common CRM/SaaS implementation issues; cross-verified with Phase 2 decision log (e.g., global query filter gotchas). Tenant isolation pitfall directly applies to API key storage in central DB.
- **Duplicate Handling (HIGH):** Reuses Phase 2 IDuplicateDetectionService with different behaviors per channel (all from CONTEXT.md D-13/D-14/D-15/D-16).

**Research date:** 2026-03-21

**Valid until:** 2026-04-21 (30 days; CsvHelper, .NET framework rate limiting, Hangfire are stable; honeypot patterns are evergreen; CSV import workflows unlikely to change)

---

*Phase: 03-lead-ingestion*
*Research completed: 2026-03-21*
