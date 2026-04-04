# IronMonkey — Lead Management System

## What This Is

A multi-tenant, domain-agnostic Lead Management SaaS platform. Any business — automobile dealerships, real estate agencies, insurance brokers, service providers, educational institutions — can sign up as a tenant and fully configure the system to match their lead workflow without any code changes. Built on .NET Aspire with Blazor Server frontend.

**Current State:** v1.3 shipped 2026-04-03. 15 phases across 4 milestones, 61 plans, 143+ integration tests. Full multi-tenant CRM with configurable leads, multi-channel ingestion, pipeline workflow engine, reporting dashboards, industry recipe onboarding, complete Blazor Server admin UI, public SaaS landing page, and tenant self-service signup flow. Built on .NET 10.0, .NET Aspire 13.1, EF Core 10.0.5, PostgreSQL, Tailwind CSS v4 (standalone CLI).

## Core Value

Any business can configure their complete lead management workflow — fields, statuses, pipelines, automation rules — without writing code or contacting support.

## Requirements

### Validated

- ✓ .NET Aspire orchestration with AppHost — existing
- ✓ Blazor Server frontend project structure — existing
- ✓ Minimal API service with endpoint routing — existing
- ✓ Entity Framework Core with multi-tenant entity model — existing
- ✓ ASP.NET Identity authentication foundation — existing
- ✓ Fluent Validation for request validation — existing
- ✓ Domain event outbox pattern — existing
- ✓ Multi-tenancy with DB-per-tenant isolation — v1.0 (TNCY-01, TNCY-02)
- ✓ Configurable lead fields (custom fields per tenant) — v1.0 (LEAD-01)
- ✓ Configurable lead statuses and pipelines — v1.0 (LEAD-02, LEAD-03)
- ✓ Duplicate detection and lead merge — v1.0 (LEAD-04, LEAD-05)
- ✓ Lead ingestion: manual entry with duplicate warning — v1.0 (INGST-01)
- ✓ Lead ingestion: REST API with tenant API keys — v1.0 (INGST-03)
- ✓ Lead ingestion: bulk CSV import with error reporting — v1.0 (INGST-02)
- ✓ Lead ingestion: embeddable web forms with honeypot protection — v1.0 (INGST-04)
- ✓ State machine for lead lifecycle transitions — v1.0 (PIPE-05)
- ✓ Workflow engine with triggers and conditional logic — v1.0 (PIPE-04)
- ✓ Auto-actions (assign, notify, schedule follow-up) — v1.0 (PIPE-04)
- ✓ Kanban pipeline board with drag-drop — v1.0 (PIPE-01)
- ✓ Task management linked to leads — v1.0 (PIPE-02)
- ✓ Lead routing (round-robin, territory) — v1.0 (PIPE-03)
- ✓ Basic dashboards: pipeline overview, conversion rates, agent performance — v1.0 (REPT-01, REPT-02, REPT-03)
- ✓ Unified lead activity timeline — v1.0 (ACTV-01)
- ✓ Industry recipe data model with reusable templates — v1.1 (RCPE-01..04)
- ✓ Blank/Custom option for tenants without a matching industry — v1.1 (RCPE-03)
- ✓ Recipe application seeds tenant database on provisioning — v1.1 (ONBD-03)
- ✓ Automobile Dealership recipe (stages, fields, rules, roles, sample leads) — v1.1 (RCNT-01, RCNT-03)
- ✓ Educational Institution recipe (stages, fields, rules, roles, sample leads) — v1.1 (RCNT-02, RCNT-03)
- ✓ Recipe selection during tenant signup/provisioning — v1.1 (ONBD-01)
- ✓ Recipe preview and catalog API — v1.1 (ONBD-02, ONBD-05)
- ✓ Recipe administration (create, update, deactivate) — v1.1 (RADM-01..03)
- ✓ Tenant can freely modify all recipe-seeded configuration — v1.1 (ONBD-04)

- ✓ UI foundation with Tailwind CSS and Blazor Server layout — v1.2 (UIFN-01..06)
- ✓ Login page with JWT authentication — v1.2 (UIFN-01)
- ✓ Role-based route protection and auth guards — v1.2 (UIFN-02)
- ✓ Recipe management admin pages (list, create, edit, preview, deactivate) — v1.2 (RCUI-01..05)
- ✓ Tenant management admin pages (view, approve/reject signups) — v1.2 (TNUI-01..04)
- ✓ User & role management admin pages — v1.2 (USUI-01..04)
- ✓ System configuration UI (pipeline stages, custom fields, routing, workflow rules) — v1.2 (CFUI-01..04)

- ✓ Public SaaS landing page with product feature showcase — v1.3 (LAND-01..04)
- ✓ Tenant self-service signup form with recipe selection — v1.3 (SIGN-01..04)
- ✓ Navigation between landing, login, and signup pages — v1.3 (NAV-01, NAV-02)

### Active

(None — next milestone not yet defined)

### Future

- [ ] Configurable customer fields (custom fields per tenant)
- [ ] Custom roles with granular permissions per tenant
- [ ] Employee/team management within tenant
- [ ] Lead ingestion: auto-capture (email parsing, phone logs, social)
- [ ] Omnichannel communications: email to leads
- [ ] Omnichannel communications: SMS to leads
- [ ] Omnichannel communications: WhatsApp Business API
- [ ] Production SaaS: tenant signup and onboarding flow (partially addressed in v1.3)
- [ ] Production SaaS: API documentation
- [ ] Production SaaS: billing integration (model TBD)

### Out of Scope

- Custom report builder — basic dashboards sufficient for v1
- Mobile native app — Blazor Server is web-first
- Real-time chat with leads — defer to future
- AI lead scoring — defer to future
- Marketplace for third-party integrations — defer to future
- Video/voice calling — out of scope entirely
- End-user CRM pages (Kanban, lead management, dashboards) — deferred to later milestone
- Node.js toolchain for Tailwind — using standalone CLI instead

## Context

Shipped v1.3 with 15 phases across 4 milestones. Tech stack: .NET 10.0, .NET Aspire 13.1, EF Core 10.0.5, PostgreSQL (Npgsql 10.0.1), Hangfire, Serilog, BCrypt, FuzzySharp, CsvHelper, Tailwind CSS v4 (standalone CLI). 143+ integration tests using Testcontainers (postgres:15-alpine). v1.3 added public-facing SaaS landing page and tenant signup flow — 7 new Blazor components (~636 LOC). Blazor Server admin UI complete; public pages use separate PublicLayout.razor.

The system must be truly domain-agnostic — the data model for leads, statuses, workflows, and fields is entirely tenant-defined. Industry "recipes" (automobile, real estate, insurance, etc.) provide sensible defaults but everything is customizable.

## Constraints

- **Tech stack**: .NET Aspire + Blazor Server + EF Core — build on existing foundation
- **Database**: DB-per-tenant for full data isolation — each tenant gets their own database
- **Configurability**: Zero-code — all customization through UI, no tenant-specific code
- **Communications**: Must support email, SMS, and WhatsApp — third-party provider integrations required

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| DB-per-tenant isolation | Maximum data isolation, compliance-friendly, tenant can be migrated independently | ✓ Good — working well across all phases |
| Blazor Server (not WASM) | Simpler auth, no API duplication, real-time updates via SignalR | — Pending (frontend not yet connected) |
| Industry recipes for onboarding | Reduces time-to-value for new tenants, avoids blank-slate problem | ✓ Good — shipped v1.1, 3 recipes (Blank, Auto, Edu) |
| Recipes as starting points, not locked | Tenant freedom to customize post-provisioning | ✓ Good — all seeded entities are mutable |
| Seed existing roles (no granular permissions) | Keep v1.1 focused; granular permissions deferred | ✓ Good — roles informational only, permissions deferred |
| JSONB recipe content model | Store stages/fields/rules/roles as JSONB snapshot, copy at provisioning | ✓ Good — clean separation between template and tenant data |
| Fixed GUID for Blank recipe | Deterministic provisioning without DB lookup | ✓ Good — simplifies fallback logic |
| AllowAnonymous for recipe browsing | Signup flow needs recipe list before auth | ✓ Good — public GET, admin-only mutations |
| Billing model deferred | Not finalized — will decide between subscription tiers, usage-based, or hybrid | — Pending |
| JSONB custom fields with HasConversion | Flexible tenant-defined fields without schema changes | ✓ Good — EF Core value converters work cleanly |
| Outbox pattern for domain events | Reliable async processing, integrated with Hangfire | ✓ Good — template for all background work |
| ActivityLog via SaveChanges interceptor | Automatic audit trail without per-endpoint instrumentation | ✓ Good — zero-touch change tracking |
| Direct LINQ for dashboards (no materialized views) | Simpler v1, defer optimization | — Pending (monitor at scale) |
| Tailwind CSS via standalone CLI | No Node.js dependency, simpler build pipeline for Blazor Server | ✓ Good — v4.2.2 integrated via MSBuild BeforeTargets |
| Admin UI first, CRM pages later | Establish UI patterns and auth foundation before building user-facing pages | ✓ Good — v1.2 complete, patterns established for CRM pages |
| Inline editing for config pages | Faster admin workflow for simple config items | ✓ Good — Phase 13 SystemConfiguration uses inline edit across 4 tabs |
| Auto-generated passwords for user creation | More secure than admin-entered passwords | ✓ Good — BCrypt hashed, displayed once with copy-to-clipboard |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd:transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd:complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-04-04 after v1.3 Public Landing & Tenant Signup milestone completed*
