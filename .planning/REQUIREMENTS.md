# Requirements: IronMonkey — Lead Management System

**Defined:** 2026-03-19
**Core Value:** Any business can configure their complete lead management workflow without writing code

## v1 Requirements

Requirements for initial release. Each maps to roadmap phases.

### Multi-Tenancy

- [x] **TNCY-01**: System provisions isolated database per tenant on signup
- [x] **TNCY-02**: All queries enforce tenant isolation — no cross-tenant data leakage

### Lead Data

- [x] **LEAD-01**: Tenant can define custom fields on lead records (text, number, date, dropdown, multi-select, currency, boolean)
- [x] **LEAD-02**: Tenant can configure pipeline stages with custom names, ordering, and metadata
- [x] **LEAD-03**: Each lead tracks its source (manual, import, API, web form)
- [x] **LEAD-04**: System detects duplicate leads by fuzzy matching on email, phone, and name
- [x] **LEAD-05**: User can merge duplicate lead records

### Lead Ingestion

- [x] **INGST-01**: User can create leads manually through the UI
- [x] **INGST-02**: User can bulk import leads via CSV/Excel with field mapping and validation
- [x] **INGST-03**: Leads can be created via REST API
- [x] **INGST-04**: Tenant can generate embeddable web forms that create leads on submission

### Pipeline & Workflow

- [x] **PIPE-01**: User can view leads in a Kanban-style pipeline board with drag-drop between stages
- [x] **PIPE-02**: User can create tasks with due dates, priority, and assignment linked to leads
- [x] **PIPE-03**: System supports manual lead assignment and rule-based routing (round-robin, territory)
- [x] **PIPE-04**: Tenant can define workflow rules: triggers (field change, status change, time-based) with conditions and auto-actions
- [x] **PIPE-05**: Lead lifecycle follows a configurable state machine with allowed transitions per tenant

### Reporting

- [x] **REPT-01**: Dashboard shows pipeline overview (leads per stage, total value)
- [x] **REPT-02**: Dashboard shows conversion rates by stage, source, and time period
- [x] **REPT-03**: Dashboard shows agent performance metrics (leads handled, tasks completed, conversion rate)

### Activity

- [x] **ACTV-01**: Each lead has a unified activity timeline showing all interactions and changes

## v2 Requirements

Deferred to future release. Tracked but not in current roadmap.

### Communications

- **COMM-01**: Send emails to leads with open/click tracking
- **COMM-02**: Send SMS to leads via Twilio or similar provider
- **COMM-03**: Send WhatsApp messages via WhatsApp Business API
- **COMM-04**: Bulk communication campaigns (templated email/SMS to filtered lists)

### Onboarding & Roles

- **ONBD-01**: Industry recipe templates for quick onboarding (auto, real estate, insurance, services, education)
- **ROLE-01**: Tenant-defined custom roles with granular permissions
- **ROLE-02**: Field-level and record-level permission controls

### Advanced Features

- **ADVN-01**: Real-time notifications via SignalR for new leads, at-risk deals
- **ADVN-02**: Rule-based lead scoring (source, budget, engagement)
- **ADVN-03**: Auto-capture lead ingestion (email parsing, phone logs, social media)
- **ADVN-04**: Custom report builder with filters, grouping, export
- **ADVN-05**: Lead enrichment via third-party data providers

## Out of Scope

| Feature | Reason |
|---------|--------|
| AI lead scoring | Requires ML infrastructure, premature before rule-based scoring stabilizes |
| Mobile native app | Web-first via Blazor Server; responsive design sufficient for v1 |
| Real-time chat with leads | Becomes a separate product; defer to webhook integrations |
| Video/voice calling | Infrastructure complexity; VoIP integration deferred |
| Email/calendar sync | Deep Outlook/Google integration deferred to future |
| Marketplace for integrations | Managing third-party apps is a separate product |
| Tenant-specific custom code | Use workflow engine + API for extensibility instead |
| Hard-coded industry pipelines | Use configurable recipes, not fixed pipelines |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| TNCY-01 | Phase 1 | Complete |
| TNCY-02 | Phase 1 | Complete |
| LEAD-01 | Phase 2 | Complete |
| LEAD-02 | Phase 2 | Complete |
| LEAD-03 | Phase 2 | Complete |
| LEAD-04 | Phase 2 | Complete |
| LEAD-05 | Phase 2 | Complete |
| INGST-01 | Phase 3 | Complete |
| INGST-02 | Phase 3 | Complete |
| INGST-03 | Phase 3 | Complete |
| INGST-04 | Phase 3 | Complete |
| PIPE-01 | Phase 4 | Complete |
| PIPE-02 | Phase 4 | Complete |
| PIPE-03 | Phase 4 | Complete |
| PIPE-04 | Phase 4 | Complete |
| PIPE-05 | Phase 4 | Complete |
| REPT-01 | Phase 5 | Complete |
| REPT-02 | Phase 5 | Complete |
| REPT-03 | Phase 5 | Complete |
| ACTV-01 | Phase 5 | Complete |

**Coverage:**
- v1 requirements: 20 total
- Mapped to phases: 20
- Unmapped: 0 ✓

---
*Requirements defined: 2026-03-19*
*Last updated: 2026-03-19 — traceability filled after roadmap creation*
