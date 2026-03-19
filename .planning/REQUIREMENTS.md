# Requirements: IronMonkey — Lead Management System

**Defined:** 2026-03-19
**Core Value:** Any business can configure their complete lead management workflow without writing code

## v1 Requirements

Requirements for initial release. Each maps to roadmap phases.

### Multi-Tenancy

- [ ] **TNCY-01**: System provisions isolated database per tenant on signup
- [ ] **TNCY-02**: All queries enforce tenant isolation — no cross-tenant data leakage

### Lead Data

- [ ] **LEAD-01**: Tenant can define custom fields on lead records (text, number, date, dropdown, multi-select, currency, boolean)
- [ ] **LEAD-02**: Tenant can configure pipeline stages with custom names, ordering, and metadata
- [ ] **LEAD-03**: Each lead tracks its source (manual, import, API, web form)
- [ ] **LEAD-04**: System detects duplicate leads by fuzzy matching on email, phone, and name
- [ ] **LEAD-05**: User can merge duplicate lead records

### Lead Ingestion

- [ ] **INGST-01**: User can create leads manually through the UI
- [ ] **INGST-02**: User can bulk import leads via CSV/Excel with field mapping and validation
- [ ] **INGST-03**: Leads can be created via REST API
- [ ] **INGST-04**: Tenant can generate embeddable web forms that create leads on submission

### Pipeline & Workflow

- [ ] **PIPE-01**: User can view leads in a Kanban-style pipeline board with drag-drop between stages
- [ ] **PIPE-02**: User can create tasks with due dates, priority, and assignment linked to leads
- [ ] **PIPE-03**: System supports manual lead assignment and rule-based routing (round-robin, territory)
- [ ] **PIPE-04**: Tenant can define workflow rules: triggers (field change, status change, time-based) with conditions and auto-actions
- [ ] **PIPE-05**: Lead lifecycle follows a configurable state machine with allowed transitions per tenant

### Reporting

- [ ] **REPT-01**: Dashboard shows pipeline overview (leads per stage, total value)
- [ ] **REPT-02**: Dashboard shows conversion rates by stage, source, and time period
- [ ] **REPT-03**: Dashboard shows agent performance metrics (leads handled, tasks completed, conversion rate)

### Activity

- [ ] **ACTV-01**: Each lead has a unified activity timeline showing all interactions and changes

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
| TNCY-01 | — | Pending |
| TNCY-02 | — | Pending |
| LEAD-01 | — | Pending |
| LEAD-02 | — | Pending |
| LEAD-03 | — | Pending |
| LEAD-04 | — | Pending |
| LEAD-05 | — | Pending |
| INGST-01 | — | Pending |
| INGST-02 | — | Pending |
| INGST-03 | — | Pending |
| INGST-04 | — | Pending |
| PIPE-01 | — | Pending |
| PIPE-02 | — | Pending |
| PIPE-03 | — | Pending |
| PIPE-04 | — | Pending |
| PIPE-05 | — | Pending |
| REPT-01 | — | Pending |
| REPT-02 | — | Pending |
| REPT-03 | — | Pending |
| ACTV-01 | — | Pending |

**Coverage:**
- v1 requirements: 20 total
- Mapped to phases: 0
- Unmapped: 20 ⚠️

---
*Requirements defined: 2026-03-19*
*Last updated: 2026-03-19 after initial definition*
