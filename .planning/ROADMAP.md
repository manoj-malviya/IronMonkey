# Roadmap: IronMonkey — Lead Management System

## Milestones

- v1.0 MVP — Phases 1-5 (shipped 2026-03-24) | [Archive](milestones/v1.0-ROADMAP.md)
- v1.1 Tenant Onboarding with Industry Recipes — Phases 6-8 (shipped 2026-03-27) | [Archive](milestones/v1.1-ROADMAP.md)
- v1.2 Admin UI — Phases 9-13 (in progress)

## Phases

<details>
<summary>v1.0 MVP (Phases 1-5) — SHIPPED 2026-03-24</summary>

- [x] Phase 1: Multi-Tenancy Foundation (6/6 plans) — completed 2026-03-20
- [x] Phase 2: Configurable Lead Model (6/6 plans) — completed 2026-03-20
- [x] Phase 3: Lead Ingestion (7/7 plans) — completed 2026-03-21
- [x] Phase 4: Pipeline & Workflow Engine (6/6 plans) — completed 2026-03-22
- [x] Phase 5: Activity & Reporting (5/5 plans) — completed 2026-03-24

</details>

<details>
<summary>v1.1 Tenant Onboarding with Industry Recipes (Phases 6-8) — SHIPPED 2026-03-27</summary>

- [x] Phase 6: Recipe Data Model (3/3 plans) — completed 2026-03-26
- [x] Phase 7: Recipe Content (3/3 plans) — completed 2026-03-26
- [x] Phase 8: Onboarding & Admin API (3/3 plans) — completed 2026-03-26

</details>

### v1.2 Admin UI (In Progress)

**Milestone Goal:** Build the Blazor Server admin interface with Tailwind CSS, connecting to existing backend APIs for full admin functionality.

- [x] **Phase 9: UI Foundation & Auth** - Tailwind CSS setup, layout shell with sidebar navigation, login page, auth guards, and logout (completed 2026-04-01)
- [x] **Phase 10: Recipe Management UI** - Admin pages to list, create, edit, preview, and deactivate industry recipes (completed 2026-04-01)
- [x] **Phase 11: Tenant Management UI** - Admin pages to view tenants and approve or reject signup requests (completed 2026-04-01)
- [x] **Phase 12: User & Role Management UI** - Admin pages to view, create, edit, and deactivate users with role assignment (completed 2026-04-01)
- [ ] **Phase 13: System Configuration UI** - Admin pages for pipeline stages, custom fields, lead routing, and workflow rules

## Phase Details

### Phase 9: UI Foundation & Auth
**Goal**: Admins can authenticate into a styled, navigable admin shell that protects all routes from unauthenticated access
**Depends on**: Phases 1-8 (existing backend API)
**Requirements**: UIFN-01, UIFN-02, UIFN-03, UIFN-04, UIFN-05, UIFN-06
**Success Criteria** (what must be TRUE):
  1. Admin can submit email and password on the login page and receive a JWT-authenticated session that persists in the browser
  2. Navigating to any admin route without a valid session redirects to the login page
  3. Admin sees a sidebar with grouped navigation links to all admin sections after logging in
  4. The sidebar collapses on smaller screens and the layout remains usable
  5. Admin can click logout and is returned to the login page with the session cleared
**Plans**: 3 plans
Plans:
- [x] 09-01-PLAN.md — Tailwind CSS setup + MSBuild integration + AppHost wire-up
- [x] 09-02-PLAN.md — JWT auth infrastructure (AuthStateProvider, BearerTokenHandler, Program.cs)
- [x] 09-03-PLAN.md — Login page, admin shell layout, sidebar navigation, auth guards
**UI hint**: yes

### Phase 10: Recipe Management UI
**Goal**: Admins can fully manage industry recipes from the browser, including creating, editing, previewing, and deactivating them
**Depends on**: Phase 9
**Requirements**: RCUI-01, RCUI-02, RCUI-03, RCUI-04, RCUI-05
**Success Criteria** (what must be TRUE):
  1. Admin can view a table of all industry recipes showing name, industry, and status
  2. Admin can fill out a form to create a new recipe with name, industry, and content (stages, fields, rules, roles) and see it appear in the list
  3. Admin can open an existing recipe, modify its details or content, and save the changes
  4. Admin can open a preview of a recipe and see its full configuration (stages, fields, rules) before applying it
  5. Admin can deactivate a recipe and see its status update to disabled in the list
**Plans**: 3 plans
Plans:
- [x] 10-01-PLAN.md — RecipeList page with status filter, table, and inline deactivate action
- [x] 10-02-PLAN.md — RecipeContentEditor shared component + RecipeCreate page with POST
- [x] 10-03-PLAN.md — RecipeEdit page (pre-populated form + PUT) + RecipePreview page (tabbed read-only)
**UI hint**: yes

### Phase 11: Tenant Management UI
**Goal**: Admins can monitor all tenants and action pending signup requests from the browser
**Depends on**: Phase 9
**Requirements**: TNUI-01, TNUI-02, TNUI-03, TNUI-04
**Success Criteria** (what must be TRUE):
  1. Admin can view a list of all tenants showing name, status, and creation date
  2. Admin can open a pending signup request and see its submitted details
  3. Admin can approve a pending signup request and see the tenant provisioning triggered
  4. Admin can reject a pending signup request by entering a reason, and the request is marked rejected
**Plans**: 3 plans
Plans:
- [x] 11-01-PLAN.md — Backend: ListTenantsEndpoint + GetSignupRequestEndpoint + integration tests
- [x] 11-02-PLAN.md — Frontend: TenantManagement.razor two-tab page with tenant list and signup request list with expandable rows
- [x] 11-03-PLAN.md — Frontend: Approve/Reject modals with two-step approve+provision chain and optimistic UI updates
**UI hint**: yes

### Phase 12: User & Role Management UI
**Goal**: Admins can manage user accounts and role assignments within a tenant from the browser
**Depends on**: Phase 9
**Requirements**: USUI-01, USUI-02, USUI-03, USUI-04
**Success Criteria** (what must be TRUE):
  1. Admin can view a list of all users in the current tenant showing name, email, role, and status
  2. Admin can create a new user by entering name, email, and selecting a role, and see the user appear in the list
  3. Admin can open an existing user, update their details or role, and save the changes
  4. Admin can deactivate a user and see their status change to disabled in the list
**Plans**: 3 plans
Plans:
- [x] 12-01-PLAN.md — Backend: User entity mutations + 6 tenant-scoped user management endpoints + Endpoints.cs registration
- [x] 12-02-PLAN.md — Frontend: UserList.razor with data table, filter toggle, and deactivate confirmation modal
- [ ] 12-03-PLAN.md — Frontend: UserPasswordDisplay shared component + UserCreate.razor + UserEdit.razor
**UI hint**: yes

### Phase 13: System Configuration UI
**Goal**: Admins can configure pipeline stages, custom fields, lead routing, and workflow rules for their tenant without writing code
**Depends on**: Phase 9
**Requirements**: CFUI-01, CFUI-02, CFUI-03, CFUI-04
**Success Criteria** (what must be TRUE):
  1. Admin can view pipeline stages and create, edit, reorder, or delete them from the browser
  2. Admin can view custom field definitions and create, edit, or delete them from the browser
  3. Admin can view and modify lead routing configuration including round-robin and territory rules
  4. Admin can view workflow rules and create, edit, or delete triggers, conditions, and actions
**Plans**: 4 plans
Plans:
- [ ] 13-01-PLAN.md — Backend: 4 missing DELETE/PUT endpoints (pipeline stage, custom field, workflow rule) + Endpoints.cs registration
- [ ] 13-02-PLAN.md — Frontend: SystemConfiguration.razor page shell + Pipeline Stages tab (inline CRUD, reorder, delete)
- [ ] 13-03-PLAN.md — Frontend: Custom Fields tab (type-conditional options) + Routing tab (mode selector + territory JSON)
- [ ] 13-04-PLAN.md — Frontend: Workflow Rules tab (trigger/action dropdowns, IsActive toggle) + AdminSidebar nav link
**UI hint**: yes

## Progress

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1. Multi-Tenancy Foundation | v1.0 | 6/6 | Complete | 2026-03-20 |
| 2. Configurable Lead Model | v1.0 | 6/6 | Complete | 2026-03-20 |
| 3. Lead Ingestion | v1.0 | 7/7 | Complete | 2026-03-21 |
| 4. Pipeline & Workflow Engine | v1.0 | 6/6 | Complete | 2026-03-22 |
| 5. Activity & Reporting | v1.0 | 5/5 | Complete | 2026-03-24 |
| 6. Recipe Data Model | v1.1 | 3/3 | Complete | 2026-03-26 |
| 7. Recipe Content | v1.1 | 3/3 | Complete | 2026-03-26 |
| 8. Onboarding & Admin API | v1.1 | 3/3 | Complete | 2026-03-26 |
| 9. UI Foundation & Auth | v1.2 | 3/3 | Complete   | 2026-04-01 |
| 10. Recipe Management UI | v1.2 | 3/3 | Complete    | 2026-04-01 |
| 11. Tenant Management UI | v1.2 | 3/3 | Complete    | 2026-04-01 |
| 12. User & Role Management UI | v1.2 | 2/3 | Complete    | 2026-04-01 |
| 13. System Configuration UI | v1.2 | 0/4 | Not started | - |
