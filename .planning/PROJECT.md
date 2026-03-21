# IronMonkey — Lead Management System

## What This Is

A multi-tenant, domain-agnostic Lead Management SaaS platform. Any business — automobile dealerships, real estate agencies, insurance brokers, service providers, educational institutions — can sign up as a tenant and fully configure the system to match their lead workflow without any code changes. Built on .NET Aspire with Blazor Server frontend.

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
- ✓ Multi-tenancy with DB-per-tenant isolation — Validated in Phase 1
- ✓ Configurable lead fields (custom fields per tenant) — Validated in Phase 2 (LEAD-01)
- ✓ Configurable lead statuses and pipelines — Validated in Phase 2 (LEAD-02, LEAD-03)
- ✓ Duplicate detection and lead merge — Validated in Phase 2 (LEAD-04, LEAD-05)

### Active

- [ ] Tenant onboarding with industry recipe/template selection
- [ ] Configurable customer fields (custom fields per tenant)
- [ ] State machine for lead lifecycle transitions
- [ ] Workflow engine with triggers and conditional logic
- [ ] Auto-actions (assign, notify, schedule follow-up)
- [ ] Custom roles with granular permissions per tenant
- [ ] Employee/team management within tenant
- [ ] Lead ingestion: manual entry via UI
- [ ] Lead ingestion: web forms and REST API
- [ ] Lead ingestion: bulk CSV/Excel import
- [ ] Lead ingestion: auto-capture (email parsing, phone logs, social)
- [ ] Omnichannel communications: email to leads
- [ ] Omnichannel communications: SMS to leads
- [ ] Omnichannel communications: WhatsApp Business API
- [ ] Basic dashboards: pipeline overview, conversion rates, agent performance
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

The existing codebase has a .NET Aspire foundation with separate API service and Blazor Server frontend. Entity Framework Core is configured with SQLite (will need to evolve for DB-per-tenant with a production database). ASP.NET Identity is in place for authentication. The architecture follows a layered pattern with Minimal APIs, domain entities, and Fluent Validation.

The system must be truly domain-agnostic — the data model for leads, statuses, workflows, and fields is entirely tenant-defined. Industry "recipes" (automobile, real estate, insurance, etc.) provide sensible defaults but everything is customizable.

## Constraints

- **Tech stack**: .NET Aspire + Blazor Server + EF Core — build on existing foundation
- **Database**: DB-per-tenant for full data isolation — each tenant gets their own database
- **Configurability**: Zero-code — all customization through UI, no tenant-specific code
- **Communications**: Must support email, SMS, and WhatsApp — third-party provider integrations required

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| DB-per-tenant isolation | Maximum data isolation, compliance-friendly, tenant can be migrated independently | — Pending |
| Blazor Server (not WASM) | Simpler auth, no API duplication, real-time updates via SignalR | — Pending |
| Industry recipes for onboarding | Reduces time-to-value for new tenants, avoids blank-slate problem | — Pending |
| Billing model deferred | Not finalized — will decide between subscription tiers, usage-based, or hybrid | — Pending |

---
*Last updated: 2026-03-21 after Phase 2 completion*
