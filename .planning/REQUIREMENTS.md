# Requirements: IronMonkey — Lead Management System

**Defined:** 2026-03-24
**Core Value:** Any business can configure their complete lead management workflow without writing code

## v1.1 Requirements

Requirements for Tenant Onboarding with Industry Recipes. Each maps to roadmap phases.

### Recipe Data Model

- [ ] **RCPE-01**: System stores industry recipe templates with pipeline stages, custom fields, workflow rules, and default roles as reusable JSONB definitions in the central database
- [ ] **RCPE-02**: Each recipe has metadata (name, description, icon/identifier) for display during selection
- [ ] **RCPE-03**: A "Blank/Custom" recipe exists with minimal defaults (one default stage, Admin role) for tenants without a matching industry
- [ ] **RCPE-04**: Recipes are versioned so changes to a recipe template can be tracked over time

### Recipe Content

- [ ] **RCNT-01**: Automobile Dealership recipe includes road-to-the-sale pipeline stages, vehicle-specific custom fields, follow-up workflow rules, and sales team roles
- [ ] **RCNT-02**: Educational Institution recipe includes admissions funnel stages, student-specific custom fields, notification workflow rules, and admissions team roles
- [ ] **RCNT-03**: Each recipe includes sample lead data so the tenant sees a working pipeline immediately after provisioning

### Onboarding Integration

- [ ] **ONBD-01**: User can select an industry recipe during tenant signup
- [ ] **ONBD-02**: User can preview what a recipe includes (stages, fields, rules) before committing to it
- [ ] **ONBD-03**: Tenant provisioning atomically applies the selected recipe — seeding stages, fields, rules, roles, and sample data in a single transaction
- [ ] **ONBD-04**: All recipe-seeded configuration is fully modifiable by the tenant after provisioning
- [ ] **ONBD-05**: System provides a list of available recipes via API endpoint

### Recipe Administration

- [ ] **RADM-01**: Admin can create new industry recipes via API
- [ ] **RADM-02**: Admin can update existing recipe definitions via API
- [ ] **RADM-03**: Admin can deactivate (soft-delete) a recipe so it no longer appears in the selection list

## Future Requirements

Deferred to future milestones. Tracked but not in current roadmap.

### Communications

- **COMM-01**: Send emails to leads with open/click tracking
- **COMM-02**: Send SMS to leads via Twilio or similar provider
- **COMM-03**: Send WhatsApp messages via WhatsApp Business API
- **COMM-04**: Bulk communication campaigns (templated email/SMS to filtered lists)

### Roles & Permissions

- **ROLE-01**: Tenant-defined custom roles with granular permissions
- **ROLE-02**: Field-level and record-level permission controls

### Advanced Features

- **ADVN-01**: Real-time notifications via SignalR for new leads, at-risk deals
- **ADVN-02**: Rule-based lead scoring (source, budget, engagement)
- **ADVN-03**: Auto-capture lead ingestion (email parsing, phone logs, social media)
- **ADVN-04**: Custom report builder with filters, grouping, export
- **ADVN-05**: Lead enrichment via third-party data providers

### Production SaaS

- **PROD-01**: Tenant signup and onboarding flow (production-ready)
- **PROD-02**: API documentation
- **PROD-03**: Billing integration (model TBD)

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| AI lead scoring | Requires ML infrastructure, premature before rule-based scoring stabilizes |
| Mobile native app | Web-first via Blazor Server; responsive design sufficient |
| Real-time chat with leads | Becomes a separate product; defer to webhook integrations |
| Video/voice calling | Infrastructure complexity; VoIP integration deferred |
| Email/calendar sync | Deep Outlook/Google integration deferred to future |
| Marketplace for integrations | Managing third-party apps is a separate product |
| Tenant-specific custom code | Use workflow engine + API for extensibility instead |
| Recipe upgrade/migration for existing tenants | v1.1 recipes are initial provisioning only; upgrade tooling deferred |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| RCPE-01 | Phase 6 | Pending |
| RCPE-02 | Phase 6 | Pending |
| RCPE-03 | Phase 6 | Pending |
| RCPE-04 | Phase 6 | Pending |
| RCNT-01 | Phase 7 | Pending |
| RCNT-02 | Phase 7 | Pending |
| RCNT-03 | Phase 7 | Pending |
| ONBD-01 | Phase 8 | Pending |
| ONBD-02 | Phase 8 | Pending |
| ONBD-03 | Phase 8 | Pending |
| ONBD-04 | Phase 8 | Pending |
| ONBD-05 | Phase 8 | Pending |
| RADM-01 | Phase 8 | Pending |
| RADM-02 | Phase 8 | Pending |
| RADM-03 | Phase 8 | Pending |

**Coverage:**
- v1.1 requirements: 15 total
- Mapped to phases: 15
- Unmapped: 0

---
*Requirements defined: 2026-03-24*
*Last updated: 2026-03-24 — traceability filled after roadmap creation*
