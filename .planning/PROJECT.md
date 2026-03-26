# IronMonkey — Lead Management System

## What This Is

A multi-tenant, domain-agnostic Lead Management SaaS platform. Any business — automobile dealerships, real estate agencies, insurance brokers, service providers, educational institutions — can sign up as a tenant and fully configure the system to match their lead workflow without any code changes. Built on .NET Aspire with Blazor Server frontend.

**Current State:** v1.1 in progress. Phase 6 (Recipe Data Model) complete — IndustryRecipe entity, JSONB content storage, Blank recipe seed, provisioning integration. 117+ integration tests. Building on v1.0 MVP (multi-tenancy, configurable lead model, multi-channel ingestion, pipeline workflow engine, reporting dashboards).

## Current Milestone: v1.1 Tenant Onboarding with Industry Recipes

**Goal:** New tenants pick an industry (or blank) at signup and get a fully pre-configured workspace — pipeline stages, custom fields, workflow rules, and default roles — as a starting point they can freely customize.

**Target features:**
- Industry recipe data model (reusable templates storing stages, fields, rules, roles)
- Two initial recipes: Automobile Dealerships, Educational Institutions
- Blank/Custom option for tenants without a matching industry
- Recipe selection integrated into tenant signup/provisioning flow
- Recipe application seeds tenant DB with pre-configured data
- Tenant can modify all recipe-seeded data after provisioning

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

### Active (v1.1)

- ✓ Industry recipe data model with reusable templates — v1.1 Phase 6
- ✓ Blank/Custom option for tenants without a matching industry — v1.1 Phase 6
- ✓ Recipe application seeds tenant database on provisioning — v1.1 Phase 6
- [ ] Automobile Dealership recipe (stages, fields, rules, roles)
- [ ] Educational Institution recipe (stages, fields, rules, roles)
- [ ] Recipe selection during tenant signup/provisioning
- [ ] Tenant can freely modify all recipe-seeded configuration

### Future

- [ ] Configurable customer fields (custom fields per tenant)
- [ ] Custom roles with granular permissions per tenant
- [ ] Employee/team management within tenant
- [ ] Lead ingestion: auto-capture (email parsing, phone logs, social)
- [ ] Omnichannel communications: email to leads
- [ ] Omnichannel communications: SMS to leads
- [ ] Omnichannel communications: WhatsApp Business API
- [ ] Production SaaS: tenant signup and onboarding flow
- [ ] Production SaaS: API documentation
- [ ] Production SaaS: billing integration (model TBD)

### Out of Scope

- Custom report builder — basic dashboards sufficient for v1
- Mobile native app — Blazor Server is web-first
- Real-time chat with leads — defer to future
- AI lead scoring — defer to future
- Marketplace for third-party integrations — defer to future
- Video/voice calling — out of scope entirely

## Context

Shipped v1.0 with 19,100 LOC across 207 C# files. Tech stack: .NET 10.0, .NET Aspire 13.1, EF Core 10.0.5, PostgreSQL (Npgsql 10.0.1), Hangfire, Serilog, BCrypt, FuzzySharp, CsvHelper. 108+ integration tests using Testcontainers (postgres:15-alpine). All API endpoints are backend-only (Minimal API); Blazor Server frontend exists but is not yet wired to Phase 2-5 endpoints.

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
| Industry recipes for onboarding | Reduces time-to-value for new tenants, avoids blank-slate problem | — In Progress (v1.1) |
| Recipes as starting points, not locked | Tenant freedom to customize post-provisioning | — Pending |
| Seed existing roles (no granular permissions) | Keep v1.1 focused; granular permissions deferred | — Pending |
| Billing model deferred | Not finalized — will decide between subscription tiers, usage-based, or hybrid | — Pending |
| JSONB custom fields with HasConversion | Flexible tenant-defined fields without schema changes | ✓ Good — EF Core value converters work cleanly |
| Outbox pattern for domain events | Reliable async processing, integrated with Hangfire | ✓ Good — template for all background work |
| ActivityLog via SaveChanges interceptor | Automatic audit trail without per-endpoint instrumentation | ✓ Good — zero-touch change tracking |
| Direct LINQ for dashboards (no materialized views) | Simpler v1, defer optimization | — Pending (monitor at scale) |

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
*Last updated: 2026-03-26 after Phase 6 complete*
