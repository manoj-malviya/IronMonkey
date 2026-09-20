# Privacy, Consent and Data Retention

## Context

This CRM stores personal data about people who never signed up for it — leads are, by definition, third parties whose names, mobile numbers and email addresses a tenant collected elsewhere. Several target verticals are regulated: a university holds applicant records, a clinic holds patient referrals, a broker holds financial data. GDPR, and comparable regimes, make the tenant the controller and the platform the processor, and they oblige both.

The system currently has no privacy layer at all. There is no consent record, no lawful-basis field, no retention policy for lead or contact data, no erasure mechanism, and no export of a single person's data. Soft delete exists — the global query filter applies `!IsDeleted` — which means "delete" today hides a row and keeps the personal data indefinitely. That is the opposite of what an erasure request requires.

Two retention mechanisms already exist and are the pattern to follow: `WorkflowExecutionMaintenanceJob` runs hourly, marks stale rows `Abandoned` and applies a retention window; `WorkflowDiagnosticRedactor` strips URLs, query strings and email local-parts from anything persisted. Both were built for one feature. Privacy needs them generalized.

## Prompt

Add a tenant-configurable privacy layer covering consent, retention, erasure and subject access, so a regulated tenant can operate on this platform without bespoke work.

### Consent and lawful basis

- Record consent per person and per purpose — marketing email, SMS, WhatsApp, profiling — with the source, timestamp, and the text or version the person agreed to. A single blanket basis across all processing is the common audit failure; model purposes separately.
- Capture consent at the points data enters: web forms, CSV import, the REST ingestion API, and manual entry. An import that carries no consent information must be explicit about that rather than defaulting to consented.
- Enforce consent at send time in the communications layer, in one place, for workflow-triggered sends as well as manual ones. Consent and opt-out are the same gate seen from two directions — do not build a second suppression list beside the one in the communications work.
- Record withdrawal of consent as an event, not an overwrite. Proving *when* consent ended is the point.

### Retention and deletion

- Let a tenant define retention periods per record type, with a platform-level maximum the tenant cannot exceed. Apply them with a scheduled job following the `WorkflowExecutionMaintenanceJob` pattern — bounded batches, idempotent, safe to run repeatedly.
- Distinguish three outcomes explicitly: soft delete (recoverable, data retained), anonymization (row and its aggregates survive, identifiers destroyed), and hard delete (row gone). Reporting history usually argues for anonymization; an erasure request usually requires one of the latter two. Make the tenant's choice per record type explicit rather than implied.
- Anonymization must reach every copy of the identifiers, which is the hard part in this codebase: the JSONB custom field bag, `ActivityLog` entries, workflow execution logs and steps, notification messages, communication history and message bodies, import batches, duplicate-detection records and merge history. A "deleted" lead whose name is still readable in an activity row has not been deleted.
- Before destroying anything, report the blast radius the way `GET .../{id}/impact` already does for stages and fields. Irreversible deletion with no preview is how a tenant loses data it needed.
- Preserve what other law requires kept. Financial and audit records frequently outlive an erasure request; make retention policy able to express that rather than forcing a single rule over every table.
- Any raw SQL that reaches into the JSONB bag to anonymize must filter `TenantId` explicitly — the global query filters do not apply to it, and an unfiltered update here anonymizes other tenants' data irreversibly. Use `jsonb_exists(col, @key)`, never `?`.

### Subject access and portability

- Add a per-person export gathering every record referring to that individual across leads, contacts, opportunities, tasks, activities, communications and custom objects, in a machine-readable format.
- Make the export authorized, tenant-scoped, rate-limited and audited. An unaudited "export everything about a person" endpoint is a data-exfiltration tool; log every use with who ran it and why.
- Search for a subject by identifier without disclosing whether a matching record exists in another tenant — a timing or error-message difference here is a cross-tenant leak.

### Platform obligations

- Give the platform operator a record of processing activities and a per-tenant view of configured retention and consent posture, so the operator can answer a processor's questions without querying tenant databases ad hoc.
- Tenant deletion must actually destroy the tenant database and its backups' eligibility, and must be audited. A provisioned-then-abandoned tenant currently keeps personal data forever.
- Keep the platform-admin path honest: a SuperAdmin reaching tenant personal data still goes through impersonation, and every impersonated export must be attributable to the platform user via the existing `act_as` claim and `TenantImpersonations` audit row.

## Acceptance criteria

- A tenant can define per-purpose consent, and a contact who withdrew consent receives no message on that channel, including from a workflow action.
- Consent state is captured at every ingestion path, and its absence is visible rather than assumed.
- A tenant can define retention per record type, and the scheduled job applies it in bounded, idempotent batches without exceeding the platform maximum.
- An erasure request removes or anonymizes the person's identifiers everywhere they were copied — including JSONB custom fields, activity logs, workflow history, and communications — verified by a test that asserts absence across all of them, not just the primary row.
- Destructive operations preview their impact before running.
- A subject-access export returns everything about one person, is audited, and cannot be used to probe for records in another tenant.
- Raw-SQL anonymization paths filter `TenantId` and are covered by a cross-tenant test.
- Tenant deletion destroys the tenant database and leaves an audit record.

## Likely implementation surfaces

- New consent, retention-policy and privacy-request entities plus a tenant migration
- `IronMonkey.Data/TenantDbContext.cs` and the `IsDeleted` soft-delete convention
- `WorkflowDiagnosticRedactor` and `WorkflowExecutionMaintenanceJob` as the patterns to generalize
- `IronMonkey.ApiService/Features/Leads/Ingestion/` (Csv, Api, WebForm)
- The communications layer's consent gate
- `IronMonkey.Data/Entities/ActivityLog.cs`, `WorkflowExecutionLog.cs`, `Notification.cs`, `ImportBatch.cs`, `LeadMerge.cs`
- Tenant provisioning/deprovisioning and `TenantImpersonation`
- Tests in `IronMonkey.Tests`

Do not ship legal advice or claim certification. Build the mechanisms a controller needs, and let the tenant configure its own policy.
