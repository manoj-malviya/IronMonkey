# Requirements: IronMonkey v1.2 Admin UI

**Defined:** 2026-03-27
**Core Value:** Any business can configure their complete lead management workflow without writing code or contacting support

## v1.2 Requirements

Requirements for the Admin UI milestone. Each maps to roadmap phases.

### UI Foundation

- [x] **UIFN-01**: Admin can log in with email and password and receive a JWT-authenticated session
- [x] **UIFN-02**: Unauthenticated users are redirected to login page when accessing admin routes
- [x] **UIFN-03**: Admin sees a sidebar navigation with grouped links to all admin sections
- [x] **UIFN-04**: Layout renders responsively with collapsible sidebar on smaller screens
- [x] **UIFN-05**: Tailwind CSS standalone CLI compiles styles from .razor files via MSBuild integration
- [x] **UIFN-06**: Admin can log out and is redirected to login page

### Recipe Management

- [x] **RCUI-01**: Admin can view a list of all industry recipes with name, industry, and status
- [x] **RCUI-02**: Admin can create a new recipe with name, industry, and content (stages, fields, rules, roles)
- [x] **RCUI-03**: Admin can edit an existing recipe's details and content
- [x] **RCUI-04**: Admin can preview a recipe's full configuration before applying
- [x] **RCUI-05**: Admin can deactivate a recipe (soft disable, not delete)

### Tenant Management

- [x] **TNUI-01**: Admin can view a list of all tenants with name, status, and creation date
- [x] **TNUI-02**: Admin can view a pending signup request with details
- [ ] **TNUI-03**: Admin can approve a pending signup request (triggers provisioning)
- [ ] **TNUI-04**: Admin can reject a pending signup request with a reason

### User & Role Management

- [ ] **USUI-01**: Admin can view a list of users within the current tenant
- [ ] **USUI-02**: Admin can create a new user with name, email, and role assignment
- [ ] **USUI-03**: Admin can edit a user's details and role
- [ ] **USUI-04**: Admin can deactivate a user (soft disable)

### System Configuration

- [ ] **CFUI-01**: Admin can view and manage pipeline stages (create, edit, reorder, delete)
- [ ] **CFUI-02**: Admin can view and manage custom field definitions (create, edit, delete)
- [ ] **CFUI-03**: Admin can view and manage lead routing configuration (round-robin, territory rules)
- [ ] **CFUI-04**: Admin can view and manage workflow rules (triggers, conditions, actions)

## Future Requirements

### End-User CRM Pages

- **CRMU-01**: User can view and manage leads in Kanban pipeline board
- **CRMU-02**: User can create and edit leads with custom fields
- **CRMU-03**: User can view lead activity timeline
- **CRMU-04**: User can view pipeline overview dashboard
- **CRMU-05**: User can view conversion rate dashboard
- **CRMU-06**: User can view agent performance dashboard
- **CRMU-07**: User can manage tasks linked to leads
- **CRMU-08**: User can import leads via CSV upload
- **CRMU-09**: User can configure web form embed codes

## Out of Scope

| Feature | Reason |
|---------|--------|
| End-user CRM pages (Kanban, lead management, dashboards) | Deferred to v1.3 milestone |
| Node.js/npm toolchain for Tailwind | Using standalone CLI instead |
| Real-time SignalR updates on admin pages | Nice-to-have, defer to polish phase |
| Dark mode toggle | Cosmetic, defer to future |
| Bulk operations (batch approve/reject) | Single-item operations sufficient for v1.2 |
| Custom report builder | Basic dashboards sufficient |
| AI-powered features | Out of scope entirely |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| UIFN-01 | Phase 9 | Complete |
| UIFN-02 | Phase 9 | Complete |
| UIFN-03 | Phase 9 | Complete |
| UIFN-04 | Phase 9 | Complete |
| UIFN-05 | Phase 9 | Complete |
| UIFN-06 | Phase 9 | Complete |
| RCUI-01 | Phase 10 | Complete |
| RCUI-02 | Phase 10 | Complete |
| RCUI-03 | Phase 10 | Complete |
| RCUI-04 | Phase 10 | Complete |
| RCUI-05 | Phase 10 | Complete |
| TNUI-01 | Phase 11 | Complete |
| TNUI-02 | Phase 11 | Complete |
| TNUI-03 | Phase 11 | Pending |
| TNUI-04 | Phase 11 | Pending |
| USUI-01 | Phase 12 | Pending |
| USUI-02 | Phase 12 | Pending |
| USUI-03 | Phase 12 | Pending |
| USUI-04 | Phase 12 | Pending |
| CFUI-01 | Phase 13 | Pending |
| CFUI-02 | Phase 13 | Pending |
| CFUI-03 | Phase 13 | Pending |
| CFUI-04 | Phase 13 | Pending |

**Coverage:**
- v1.2 requirements: 23 total
- Mapped to phases: 23
- Unmapped: 0

---
*Requirements defined: 2026-03-27*
*Last updated: 2026-03-27 after roadmap creation*
