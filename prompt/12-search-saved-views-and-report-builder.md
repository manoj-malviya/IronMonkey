# Global Search, Saved Views and Report Builder

## Context

There is no search service in the codebase — no global search, no cross-entity lookup. `LeadList` supports a `search` query parameter over a fixed set of columns and that is the whole of it. A user who knows a phone number but not which record holds it has nowhere to type it.

Filtering is similarly fixed. `LeadList` drives `search`, `stageId`, `sort` and `page` through the URL, which is good — Back/Forward works and a link reproduces the view — but the filters themselves are hardcoded, they do not cover custom fields, and a user who builds a useful view cannot keep it. Reporting is four fixed report endpoints (Conversion, Dashboard, Performance, Pipeline); "custom report builder" is explicitly listed Out of Scope in `PROJECT.md` as "basic dashboards sufficient for v1".

That exclusion is the thing to revisit here, because it is where domain-agnosticism breaks down. A fixed report set can only report on fixed fields. Once a tenant's most important data lives in custom fields — and in custom objects — a report the platform authors in advance cannot reach it. The recipes promise each vertical its own model; fixed reports quietly promise that only the built-in columns matter.

## Prompt

Add cross-entity search, saved filter views, and a tenant-facing report builder that can reach tenant-defined data.

### Global search

- Search across leads, contacts, opportunities, tasks and custom objects from one entry point, matching names, emails, phone numbers, and tenant-selected custom fields.
- Implement it in PostgreSQL — trigram indexes and/or full-text search — rather than adding a search engine dependency. Per-tenant databases make an external index a per-tenant provisioning and reindexing problem, and the deployment story is Aspire plus PostgreSQL.
- Normalize phone numbers and emails for matching. A user types `07700 900123` and the row stores `+447700900123`; a search that only matches literally is useless to the people who need it most.
- Respect the tenant filter and record-level visibility. Search is the most common accidental disclosure path — a result list that reveals a record the user cannot open is a leak, and so is a result count that includes it.
- Bound every query: cap result counts per entity type, paginate, and apply a timeout. An unindexed `LIKE '%…%'` across every JSONB bag in a large tenant will take the database down.
- Rank results usefully and group them by type, so the answer is reachable without scrolling past every partial match.

### Saved views

- Let a user build a filter set over any field — built-in, custom, or on a custom object — combining conditions with explicit AND/OR semantics, and save it as a named view with its own column selection and sort.
- Keep the existing URL-state convention: a saved view resolves to a shareable URL, Back/Forward continues to work, and `ListReturn.Resolve` still validates any `returnUrl` as a same-origin relative path before navigating. Echoing an unvalidated return URL is an open redirect.
- Support private and shared views, with sharing constrained by the permission and visibility model. A shared view must never become a way to see records the viewer's scope excludes — apply visibility at query time, not when the view is saved.
- Allow a default view per user per list, and make an empty result distinguishable from a broken filter.
- Every sort must still end with a tiebreak on `Id`, and sort keys must stay an allow-list. Without the tiebreak, rows sharing a sort value can appear on two pages or none; an arbitrary property name would order by columns the list never exposes, and an untranslatable one throws at runtime.

### Report builder

- Let a tenant define a report: a record type, filters, grouping, aggregation (count, sum, average, min, max), and a time dimension, over built-in and custom fields alike.
- Render results as a table plus a chart appropriate to the shape of the data, and allow saving, sharing and scheduling.
- Reuse `DashboardDateRange` for every time boundary rather than writing new date logic. Ranges are half-open — `[From, ToExclusive)` — because a closed `<= end-of-day` bound drops rows in the final tick against `timestamptz` microsecond precision. A second, subtly different implementation will reintroduce exactly that bug.
- Evaluate ranges in the tenant's configured timezone so "this month" means the tenant's month.
- Compile report definitions to parameterized queries. Never concatenate tenant-supplied field names, operators or values into SQL — a report builder is a SQL-injection surface by construction, and this one runs against a database holding the tenant's entire book of business.
- Enforce cost limits: row caps, execution timeouts, and a bound on grouping cardinality. Run scheduled and exported reports as background jobs on the existing `tenant` queue rather than in the request.
- Keep the four existing fixed reports working. Migrate them to the new engine only if that is genuinely simpler than leaving them; do not break working dashboards to prove a point.

### Export

- Allow CSV/Excel export of any list, saved view or report, generated as a background job with a download the user collects, not a synchronous response that times out on a large tenant.
- Apply visibility to exports and audit every one. Export is the highest-volume disclosure path in any CRM, and the one a departing employee reaches for.

## Acceptance criteria

- A user finds a record by partial name, email or phone number — including a differently-formatted phone number — across every entity type, in one search.
- Search results, counts, saved views, reports and exports all respect tenant isolation and record visibility, with no disclosure of excluded records through counts or ranking.
- A user can save a filtered view over custom fields, share it within the permission model, return to it by URL, and use Back/Forward across filter states.
- A tenant can build, save and schedule a report grouping on a custom field, with correct boundaries in its own timezone.
- No report definition, however crafted, can inject SQL or read another tenant's data; a test asserts this against hostile field names, operators and values.
- Large searches, reports and exports are bounded by row caps and timeouts and do not block HTTP requests.
- The four existing reports still work.
- Tests cover phone/email normalization, visibility in search and exports, saved-view sharing boundaries, half-open range correctness, sort tiebreaks, injection attempts, and cost limits.

## Likely implementation surfaces

- A new search feature and PostgreSQL index migrations per tenant
- New saved-view and report-definition entities plus a tenant migration
- `IronMonkey.ApiService/Features/Reports/` and `DashboardDateRange`
- `IronMonkey.ApiService/Features/Leads/` list endpoints and `ListReturn.Resolve`
- `IronMonkey.Web/Components/Pages/Admin/Leads/LeadList.razor` and the other list pages
- Hangfire jobs on the `tenant` queue for scheduled reports and exports
- Tests in `IronMonkey.Tests`

The measure of success is that a tenant can answer a question about its own custom data without the platform shipping code. Build the engine, not more fixed reports.
