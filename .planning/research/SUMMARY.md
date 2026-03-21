# Project Research Summary: IronMonkey

**Project:** IronMonkey — Multi-Tenant Configurable Lead Management SaaS
**Domain:** B2B/B2C hybrid lead management with omnichannel communications and dynamic workflows
**Researched:** 2026-03-19
**Confidence:** HIGH

---

## Executive Summary

IronMonkey is a domain-agnostic multi-tenant lead management SaaS that requires careful balancing of three competing concerns: data isolation (database-per-tenant for regulatory compliance), configurable flexibility (tenant-defined fields, workflows, pipelines), and operational simplicity (shared migrations, templates). The research indicates a proven architecture pattern: **database-per-tenant for data isolation** + **shared orchestration database** for tenant metadata, recipes, and system configuration. The stack is mature and well-established (.NET 10, EF Core 10, PostgreSQL, Blazor Server), with no exotic dependencies. The critical challenge isn't technology selection but **execution discipline around multi-tenancy** — preventing data leakage, configuration drift, and permission creep requires explicit tenant context in every database query, background job, and authorization check. Success depends on building the multi-tenancy foundation correctly in Phase 1 rather than bolting it on later.

---

## Key Findings

### Recommended Stack

IronMonkey's technology stack should leverage the existing .NET Aspire + Blazor Server foundation with strategic additions for multi-tenancy, configurable data, and omnichannel communication:

**Core Infrastructure (Existing + Stable):**
- **.NET 10.0** — Runtime (already standardized, full Aspire support)
- **ASP.NET Core 10.0.2** — Minimal APIs for lightweight backend services
- **Blazor Server 10.0.2** — Real-time UI with built-in SignalR and server-side state
- **Entity Framework Core 10.0.2** — ORM with native JSON column support critical for dynamic fields

**Database & Multi-Tenancy:**
- **PostgreSQL 15+** — Primary database (superior JSONB support, production-grade multi-tenant operations)
- **Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1** — PostgreSQL provider with EF Core 10 JSON complex types
- **Custom Tenant Middleware + EF Core Global Query Filters** — Automatic tenant data isolation

**Critical Features & Libraries:**
- **Stateless 5.20.1** — State machine for lead lifecycle (simple, no persistence overhead)
- **Twilio 7.14.3 + Twilio.AspNet.Core 8.1.2** — Unified SMS/WhatsApp/Email API
- **MudBlazor 9.x** — Material Design components for dashboard and UI
- **Hangfire 2025 + Hangfire.PostgreSQL** — Background job processing with persistent queue
- **MediatR 12.x+** — Command/event pattern for decoupled workflow dispatch
- **FluentValidation 12.1.1** — Already in use; extend for dynamic field validation

**Key Migration Decision: SQLite → PostgreSQL**
SQLite supports only limited JSON querying and poor multi-tenant operations. PostgreSQL's JSONB provides native queryability, perfect for storing tenant-specific dynamic field values. Migration path: keep SQLite for dev/test; PostgreSQL for staging/production; EF Core handles provider differences.

### Expected Features

Research from competitive CRM analysis identifies a clear pyramid of priorities:

**Table Stakes (MVP — Non-Negotiable):**
- Lead pipeline visualization (Kanban-style, customizable stages per tenant)
- Configurable lead statuses/stages with custom field support
- Task and follow-up management (prevent leads falling through cracks)
- Lead assignment with basic routing (manual + rule-based)
- Email integration (most critical communication channel)
- SMS capability (required per PROJECT.md for omnichannel)
- Basic dashboards (pipeline health, conversion rates, agent metrics)
- Multi-tenant data isolation (absolute requirement, non-negotiable)
- Manual lead entry + CSV bulk import
- Audit logging (compliance foundation)

**Competitive Differentiators (Phase 2+):**
- Multi-channel communication (email + SMS + WhatsApp unified interface)
- Workflow automation with triggers and conditional logic
- Bulk operations with deduplication and import validation
- Real-time notifications and alerts
- Activity timeline (chronological interaction history)
- Lead enrichment and advanced scoring
- Custom report builder and ad-hoc filtering
- Industry recipe templates (reduce onboarding friction)

**Explicitly Defer (Not in v1):**
- AI/ML lead scoring (requires data quality foundation first)
- Phone integration and click-to-dial (VoIP complexity, SMS covers urgent needs)
- Email/calendar sync and Outlook plugin (nice-to-have, low adoption for MVP)
- Mobile native app (Blazor Server responsiveness sufficient)
- Custom tenant code execution (use rules engine instead)
- Marketplace for third-party integrations (REST API sufficient)

### Architecture Approach

IronMonkey requires a three-layer architecture with explicit tenant routing at every boundary:

1. **Orchestration Layer (Shared Database)** — Tenant lifecycle, user identity, system configuration, industry recipe templates. Responsibilities: tenant provisioning, user authentication, feature flags, template management.

2. **Tenant Data Layer (Database-per-Tenant, Isolated)** — Lead records, custom fields, workflows, statuses, and tenant-specific events. Each tenant has a complete isolated PostgreSQL database with identical schema; connection string is resolved per request based on JWT claims.

3. **Event-Driven Communication Layer (Async Processing)** — Outbox pattern for domain events (lead created, status changed), workflow trigger evaluation (rule engine), and omnichannel dispatch. Background services poll tenant Outbox tables, evaluate workflows, and dispatch messages to email/SMS/WhatsApp.

**Key Architectural Patterns:**
- **Scoped DbContext with Tenant Routing** — DbContext lifetime is per-request (Scoped); connection string resolved dynamically from ITenantService (extracted from JWT claims)
- **Outbox Pattern** — Domain events persisted in the same transaction as business data, then processed asynchronously
- **Hybrid Relational + EAV for Custom Fields** — Standard columns remain relational for performance; custom tenant-defined fields stored in EAV table
- **Rule Engine for Workflows** — Workflow definitions stored as JSON; conditions evaluated at runtime against current lead state
- **Message Adapter Pattern for Omnichannel** — Abstract communication channels behind common `IMessageAdapter` interface

**Major Components:**
1. **Tenant Provisioning Service** — Automated database creation, schema initialization, recipe application (Phase 1)
2. **Multi-DB DbContext Factory** — Routes requests to correct tenant database based on JWT claims (Phase 1)
3. **Configurable Lead Entity** — Supports standard columns + custom field accessors with type coercion (Phase 2)
4. **Workflow Trigger Evaluator** — Hosted service that polls Outbox, evaluates conditions, executes actions (Phase 3)
5. **Event Relay Service** — Polls all tenant Outbox tables, classifies events, routes to workflow engine or communication dispatcher (Phase 4)
6. **Communication Dispatcher** — Unified interface to email/SMS/WhatsApp adapters with template substitution and delivery tracking (Phase 4)

### Critical Pitfalls

Research identified 12 critical pitfalls. The top 5 pose greatest risk:

1. **Missing Tenant Filter in Database Queries** — A single unfiltered query exposes Tenant A's leads to Tenant B. **Prevention:** Query interceptor auto-appends tenant filter; explicit integration tests verifying cross-tenant isolation; database constraints on (TenantId, RecordId).

2. **Configuration Drift Across Tenants** — Custom workflows break; field naming inconsistencies (Lead_Score vs. LeadScore) cause failures. **Prevention:** Configuration schema validation at save time; configuration audit logs; versioning/rollback; field naming conventions enforced in UI.

3. **Database-Per-Tenant Operational Complexity** — Schema migrations fail on some tenants; rollback requires per-tenant scripts. **Prevention:** Migration orchestrator with per-tenant tracking; blue-green migrations; schema versioning; infrastructure-as-code; multi-tenant monitoring dashboard.

4. **Dynamic Fields Without Type Safety** — Workflow rules fail because custom fields stored as JSON lack type constraints. **Prevention:** Define data type system (string, number, date, boolean, enum); store field definitions separately from values; enforce validation at input; index queryable fields.

5. **Missing Tenant Context in Authorization** — User with "Manager" role in Tenant A sees metrics from Tenant B. **Prevention:** Include tenant ID in JWT claims alongside role claims; implement tenant-scoped role checks; verify in integration tests that crossing tenant boundaries fails with 403.

Additional critical pitfalls: onboarding overwhelm, workflow engine without observability, data quality spiral, communication channel failures without monitoring, permission creep/role explosion, tenant-specific bugs invisible in staging, missing tenant context in background jobs.

---

## Implications for Roadmap

Based on research dependencies and architectural patterns, the roadmap should follow this phase structure:

### Phase 1: Multi-Tenancy Foundation & Tenant Provisioning

**Rationale:** Everything depends on correct tenant isolation and routing. This must be bulletproof before any data is created.

**Delivers:**
- Tenant Provisioning Service (automated database creation, schema init)
- Multi-DB DbContext Factory with scoped lifetime and dynamic connection routing
- Tenant Resolution Middleware (extract TenantId from JWT, set scoped context)
- Event Outbox infrastructure (base for async workflows)
- Tenant-scoped Authorization (JWT claims include TenantId + Role)
- Query Interceptor (auto-appends `.Where(x => x.TenantId == currentTenant)` to all queries)

**Pitfalls Addressed:**
- Missing tenant filter in queries (Query Interceptor + integration tests)
- Missing tenant context in authorization (scoped claims-based auth)
- Missing tenant context in background jobs (establish context pattern)

**Research Flags:** None — this pattern is well-documented and proven.

---

### Phase 2: Configurable Lead Model with Dynamic Fields

**Rationale:** Lead records are the foundation for everything downstream. Custom fields must be queryable and type-safe.

**Delivers:**
- Custom Field Definitions (name, type, validation rules, constraints)
- Hybrid Lead Entity (standard relational columns + EAV table for custom fields)
- Field Validators (dynamic validation rules per field definition)
- Tenant Configuration UI (Blazor pages to define fields, statuses, pipelines)
- Recipe Application (industry templates pre-populate configuration)
- Lead CRUD API endpoints (with custom field support)

**Pitfalls Addressed:**
- Dynamic fields without type safety (enforce type system at definition time)
- Configuration drift (schema validation, audit logs)
- Data quality spiral (field validation at input, duplicate detection on lead creation)
- Onboarding overwhelm (recipe templates provide sensible defaults)

**Research Flags:**
- **Field Queryability:** Need to validate that dynamic fields used in filters/workflows are properly indexed. Research PostgreSQL JSON indexing strategies.

---

### Phase 3: Workflow Engine & Lead Lifecycle Automation

**Rationale:** Workflows (trigger → condition → action) are the primary automation mechanism. Stateless handles lead status transitions; rule engine evaluates conditions; outbox pattern enables async execution.

**Delivers:**
- Lead Status & Pipeline Definitions (tenant-defined stages, sequences)
- Workflow Definition Store (trigger type, conditions, actions)
- Rule Engine (evaluates conditions: if/then/else, AND/OR logic)
- State Machine Validator (Stateless 5.20.1 for status transition validation)
- Workflow Trigger Evaluator (Hosted Service polling Outbox)
- Task Creation and Scheduling
- Workflow Execution Tracing & Dry-Run Mode (observability)

**Pitfalls Addressed:**
- Workflow engine complexity without observability (execution tracing, dry-run mode)
- Configuration drift (workflow validation, audit logs)
- Missing tenant context in background jobs (Workflow Trigger Evaluator scoped per tenant)

**Research Flags:** None — pattern is standard and well-established.

---

### Phase 4: Event-Driven & Omnichannel Communication

**Rationale:** Event relay service connects workflow engine to communication providers. Multi-channel messaging unified through adapter pattern.

**Delivers:**
- Event Relay Service (Hosted Service polling all tenant Outbox tables)
- Communication Dispatcher (unified interface to channels)
- Email Adapter + SMTP integration
- SMS Adapter + Twilio integration
- WhatsApp Adapter + Twilio Business API
- Message Templates (with variable substitution)
- Delivery Tracking & Retry Logic
- Communication Dashboard (delivery history, failures, status)

**Pitfalls Addressed:**
- Omnichannel communication channel failures silent in production (delivery tracking, failure monitoring)
- Missing tenant context in background jobs (Event Relay scoped per tenant)

**Research Flags:** None — Twilio integration is straightforward and well-documented.

---

### Phase 5: UI & Self-Service Onboarding

**Rationale:** Users need to onboard themselves without support; recipes reduce time-to-value from weeks to hours.

**Delivers:**
- Tenant Onboarding Flow (signup → choose industry recipe → create first lead)
- Lead Dashboard (Blazor pages with custom fields visible)
- Workflow Builder UI (visual rule builder, not JSON)
- Tenant Admin Console (field management, status management, user roles)
- Bulk Import UI (CSV with validation and deduplication)

**Pitfalls Addressed:**
- Onboarding overwhelm (fast path with recipes, defer advanced config)
- Workflow engine complexity (visual builder UI instead of JSON)

**Research Flags:**
- **Onboarding Metrics:** Measure drop-off rate, time-to-first-lead, configuration completion. Test with 3+ industries to find common patterns.

---

### Phase 6: Production Hardening, Compliance & Scale

**Rationale:** System is now feature-complete; focus on reliability, security, compliance, and performance at scale.

**Delivers:**
- Database migration automation (across 10s of tenants)
- Connection pooling optimization
- Tenant isolation verification (security audit)
- Audit logging (who changed what, when, in which tenant)
- Data quality dashboard
- Compliance features (data retention policies)
- Performance testing under load
- Backup & disaster recovery automation
- Monitoring dashboard

**Pitfalls Addressed:**
- Database-per-tenant operational complexity (migration automation, monitoring)
- Tenant-specific bugs (extreme configuration test tenants, load testing)
- Permission creep (audit process for roles)

**Research Flags:** None — standard production hardening.

---

### Phase Ordering Rationale

1. **Phase 1 must come first** — All downstream work depends on correct tenant isolation.
2. **Phase 2 before Phase 3** — Lead entity with custom fields is the data model; workflows depend on accessing and validating these fields.
3. **Phase 3 before Phase 4** — Workflow definitions and outbox must be in place before communication dispatcher.
4. **Phase 4 before Phase 5** — Communication features must work before users see them in the UI.
5. **Phase 5 after core features work** — Don't invest in UI polish until backend is solid.
6. **Phase 6 after MVP validation** — Production hardening happens once you have real tenants with real usage patterns.

---

### Research Flags

Phases likely needing deeper research during planning:

- **Phase 2 — Dynamic Fields Queryability:** PostgreSQL JSON indexing for custom fields used in workflows/filters. Verify performance with 100+ custom fields per tenant.
- **Phase 5 — Onboarding Metrics:** Measure drop-off and time-to-value with real users from 3-5 industries.

Phases with standard patterns (skip research-phase):

- **Phase 1:** Multi-tenancy pattern is proven and well-documented.
- **Phase 3:** State machines and rule engines are established patterns.
- **Phase 4:** Twilio and email integrations are straightforward.
- **Phase 6:** Standard production hardening practices.

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| **Stack** | HIGH | All technologies verified with .NET 10 compatibility. EF Core 10 JSON complex types work with PostgreSQL. Stateless, Twilio, MudBlazor are stable. No exotic dependencies. |
| **Features** | HIGH | Competitive CRM analysis across Salesforce, HubSpot, Pipedrive, Zoho CRM is comprehensive. Table stakes clearly identified. Deferral decisions justified. |
| **Architecture** | HIGH | Multi-tenant database-per-tenant pattern is proven at scale. Outbox pattern, rule engine, scoped DbContext are all established. |
| **Pitfalls** | HIGH | 12 pitfalls researched from domain experts, academic papers, and production incident reports. Prevention strategies are specific and actionable. |

**Overall Confidence:** **HIGH**

Research is high-quality across all four dimensions. Stack is mature with no version compatibility issues. Features align with competitive market and PROJECT.md requirements. Architecture is proven and well-documented. Pitfalls are specific to this domain and have clear prevention strategies.

---

## Gaps to Address

Areas where research was thorough but require validation during implementation:

- **Dynamic Field Indexing Performance:** PostgreSQL JSONB indexing strategies for queryable custom fields. Test with 100+ fields per tenant to verify dashboard performance.
  - *How to handle:* Phase 2 includes performance testing. If indexing proves insufficient, consider denormalization in Phase 6.

- **Onboarding Conversion Rates:** Unknown whether 5-minute recipe-based onboarding achieves >80% completion rate. Different industries may have different expectations.
  - *How to handle:* Phase 5 includes onboarding metrics tracking. Iterate if drop-off is high.

- **Workflow Complexity Limits:** Where does the rule engine become too slow? What's the practical limit on workflow count or lead ingestion rate?
  - *How to handle:* Phase 3 includes load testing. Phase 6 documents limits and provides guidance to tenants.

- **Tenant Database Scaling:** At what tenant count does database-per-tenant become operationally unsustainable?
  - *How to handle:* Phase 1 architecture includes migration path documentation. Phase 6 includes cost/complexity analysis when tenant count exceeds 100.

- **Multi-Channel Fallback Strategy:** How should the system handle email provider failures? Should it automatically fall back to SMS, or alert the tenant?
  - *How to handle:* Phase 4 communication dispatcher includes configurable fallback logic.

---

## Sources

### Primary Research (HIGH Confidence)

**Technology Stack:**
- [Microsoft Learn: EF Core Multi-Tenancy](https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy)
- [Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1 Release Notes](https://www.npgsql.org/efcore/release-notes/10.0.html)
- [Stateless 5.20.1 NuGet Package](https://www.nuget.org/packages/stateless/)
- [Twilio .NET SDK Documentation](https://www.twilio.com/docs/libraries/csharp)
- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/)

**Architecture Patterns:**
- [Microsoft Learn: Multi-tenancy Patterns](https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy)
- [Multi-Tenant Architecture Best Practices — WorkOS](https://workos.com/blog/developers-guide-saas-multi-tenant-architecture)
- [Outbox Pattern — OneUptime](https://oneuptime.com/blog/post/2026-01-26-dotnet-outbox-pattern/view)

**Feature Landscape:**
- [Salesforce vs Zoho vs HubSpot vs Pipedrive Comparison 2026](https://blog.salesflare.com/compare-salesforce-zoho-hubspot-pipedrive)
- [CRM Features Comparison — Capterra](https://www.capterra.com/)
- [Lead Management Best Practices — Apollo.io](https://www.apollo.io/insights/sales-pipeline-tool)

**Domain Pitfalls:**
- [Multi-Tenant SaaS Architecture: Mistakes to Avoid — SaaS Adviser](https://www.saasadviser.co/blog/multi-tenant-saas-architecture-mistakes-best-practices)
- [Designing for Multi-Tenant Data Isolation — Propelius](https://propelius.ai/blogs/tenant-data-isolation-patterns-and-anti-patterns)
- [Schema Migrations in Multi-Tenant Systems — Medium](https://sollybombe.medium.com/how-to-handle-schema-migrations-safely-across-tenants-in-multi-tenant-saas-2025-edition-0c4e4fb3103b)

### Secondary Research (MEDIUM Confidence)

- [CRM Automation Best Practices — Jetpack CRM](https://jetpackcrm.com/crm-automation-guide-to-workflow-optimization-and-process-automation/)
- [Lead Data Quality Strategies — CRM Switch](https://crmswitch.com/crm-value/data-quality/)
- [SaaS Onboarding Best Practices 2026 — SaaS UI](https://www.saasui.design/blog/saas-onboarding-flows-that-actually-convert-2026)

---

*Research completed: 2026-03-19*
*Ready for roadmap generation: yes*
