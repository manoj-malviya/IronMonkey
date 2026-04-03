# Requirements: IronMonkey

**Defined:** 2026-04-03
**Core Value:** Any business can configure their complete lead management workflow — fields, statuses, pipelines, automation rules — without writing code or contacting support.

## v1.3 Requirements

Requirements for v1.3 Public Landing & Tenant Signup. Each maps to roadmap phases.

### Landing Page

- [ ] **LAND-01**: Visitor sees a hero section with product tagline, description, and CTA buttons
- [ ] **LAND-02**: Visitor sees a feature grid showcasing key capabilities (multi-tenant, configurable leads, industry recipes, pipeline management)
- [ ] **LAND-03**: Landing page is responsive across desktop, tablet, and mobile
- [ ] **LAND-04**: Landing page replaces the current Home.razor default content

### Tenant Signup

- [ ] **SIGN-01**: Visitor can submit a signup form with company name, admin email, password, phone, company size, address, and billing contact
- [ ] **SIGN-02**: Visitor can browse and preview industry recipes during signup and optionally select one
- [ ] **SIGN-03**: Visitor sees a confirmation page after successful signup submission
- [ ] **SIGN-04**: Signup form validates all required fields with inline error messages

### Navigation

- [ ] **NAV-01**: Landing page has clear navigation to login and signup pages
- [ ] **NAV-02**: Login page links to signup, signup page links to login

## Future Requirements

- Configurable customer fields (custom fields per tenant)
- Custom roles with granular permissions per tenant
- Employee/team management within tenant
- End-user CRM pages (Kanban, lead management, dashboards)
- Omnichannel communications (email, SMS, WhatsApp)
- Production SaaS: API documentation
- Production SaaS: billing integration

## Out of Scope

| Feature | Reason |
|---------|--------|
| Instant provisioning after signup | Existing flow requires admin approval — keep for v1.3 |
| Pricing page | Billing model not finalized yet |
| OAuth/social login | Email/password sufficient, defer to future |
| Animated landing page transitions | Keep it clean and fast, no motion libraries |
| End-user CRM pages | Separate milestone — v1.3 is public-facing only |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| LAND-01 | Phase 14 | Pending |
| LAND-02 | Phase 14 | Pending |
| LAND-03 | Phase 14 | Pending |
| LAND-04 | Phase 14 | Pending |
| NAV-01 | Phase 14 | Pending |
| SIGN-01 | Phase 15 | Pending |
| SIGN-02 | Phase 15 | Pending |
| SIGN-03 | Phase 15 | Pending |
| SIGN-04 | Phase 15 | Pending |
| NAV-02 | Phase 15 | Pending |

**Coverage:**
- v1.3 requirements: 10 total
- Mapped to phases: 10
- Unmapped: 0 ✓

---
*Requirements defined: 2026-04-03*
*Last updated: 2026-04-03 after roadmap creation — all 10 requirements mapped*
