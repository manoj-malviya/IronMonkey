# Feature Landscape: Lead Management SaaS

**Domain:** Multi-tenant, domain-agnostic lead management (B2B/B2C hybrid)
**Researched:** 2026-03-24 (v1.1 industry recipe features updated); 2026-03-27 (v1.2 admin UI features added)
**Confidence:** HIGH (Table stakes verified against HubSpot/Pipedrive/Salesforce implementations; recipes verified across 2026 CRM best practices, multi-tenant SaaS patterns, and industry-specific literature; admin UI patterns verified against Blazor Server multi-tenant implementations and SaaS admin dashboard best practices)

## Table Stakes

Features users expect in any serious lead management system. Missing these = product feels incomplete or unprofessional.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| **Lead pipeline visualization** | Every lead management tool displays the sales funnel as a Kanban-style pipeline with stages. Users live in this view. | Medium | Pipedrive set the standard (visual, drag-drop), now industry baseline. Customizable stages per tenant. |
| **Configurable lead statuses/stages** | Every business has different pipeline stages. "Lead → Qualified → Negotiation → Won" vs "Inquiry → Appointment → Conversion" vary by industry. | Low-Medium | Must support custom stage names, reordering, and stage metadata (probability, expected close date). |
| **Contact/lead records with custom fields** | Users need to capture industry-specific data (vehicle type, property value, policy lines, course interest). Standard fields (name, email, phone) alone are insufficient. | Medium | Core to IronMonkey's design: tenant-defined field schema. Include field validation rules, required/optional flags. |
| **Task and follow-up management** | Sales reps must track next actions: "Call at 2pm", "Send proposal", "Follow up in 3 days". Without this, leads fall through cracks. | Low-Medium | Auto-create tasks on triggers (e.g., lead assigned → create follow-up task). Support task priority, due dates, assignment. |
| **Lead assignment and routing** | New leads must go to the right person/team. Manual assignment + rule-based routing (round-robin, territory, skills). | Medium | Critical for high-volume businesses (insurance, real estate, auto sales). Rule-based routing adds value over pure manual. |
| **Lead scoring** | Prioritize high-value prospects. Rule-based scoring (fit, engagement) is baseline; AI scoring is emerging as table stakes in competitive markets. | Medium-High | Start with rule-based (lead source, budget, timeline). AI scoring deferred per PROJECT.md, but architecture must support it. |
| **Basic dashboards and reporting** | Visibility into pipeline health: total leads, conversion rates by stage, agent productivity, win-loss ratios, aging leads. | Medium | Role-based views (rep sees their metrics, manager sees team metrics, exec sees business metrics). Real-time updates crucial. |
| **Email integration/tracking** | Send emails from CRM, auto-log replies, track open/click rates. Must not require manual data entry of outbound emails. | Medium-High | Native email integration or deep SMTP/API integration with third-party providers (SendGrid, AWS SES). Track opens/clicks. |
| **SMS capability** | Quick follow-ups via text. Expected in high-touch industries (real estate, auto, insurance). | Medium | Either native or via third-party provider (Twilio, AWS SNS). Per PROJECT.md, required for MVP. |
| **Lead capture: manual entry** | Users create leads manually in the UI. Never automate this away. | Low | Standard form entry, bulk import via CSV/Excel. |
| **Lead capture: web forms** | Website visitors fill a form → auto-create lead in CRM. Zero-code form builder required. | Medium | Per PROJECT.md, deferred but foundational. Pre-built form templates per industry. |
| **Basic user authentication and roles** | Multi-tenant system requires per-user login, role-based access control (who sees what data). | Medium | ASP.NET Identity foundation exists. Support admin, manager, sales rep, read-only roles. |
| **Data isolation per tenant** | Tenant A's data must never leak to Tenant B. Hard boundary, zero exceptions. | High | Per PROJECT.md, DB-per-tenant is the architecture choice. This is non-negotiable for compliance/trust. |
| **Audit and compliance logging** | Who changed what, when, why. Essential for regulated industries (insurance, finance). | Low-Medium | Track creates/updates/deletes with user, timestamp, old/new values. Support data retention policies. |

---

## Industry Recipe & Template Features (v1.1 Focus)

Features specific to pre-configured onboarding via industry recipes. These are table stakes for modern SaaS CRM onboarding in 2026. Research shows completion of onboarding templates reduces 30-day churn by 50% (15-20% down to 7-10%) and improves activation rates to 40-60%.

| Feature | Why Expected | Complexity | Dependencies | Notes |
|---------|--------------|------------|--------------|-------|
| **Recipe selection at signup** | Users expect industry matching during account creation. Modern SaaS (Notion, HubSpot) provide role/intent questions at signup to pre-select templates. | Low | Signup/provisioning flow | Can be 3-5 radio buttons or dropdown. Reduces onboarding friction by 30%. |
| **Pre-configured pipeline stages** | Industry recipes should include realistic deal stages (auto dealership: "Lead", "Test Drive", "Negotiation", "Sold"; education: "Inquiry", "Application", "Interview", "Enrolled"). Users expect sensible defaults, not blank canvas. | Low-Medium | PipelineStage entity model, seeding logic | Seeds records with ordering, names, and metadata matching industry. Uses existing infrastructure. |
| **Pre-configured custom fields** | Recipe defines what customer data to capture per industry (auto: "Vehicle Interest", "Budget", "Trade-in Value"; education: "Degree Program", "Test Scores", "Interview Date"). Prevents "what fields do we need?" analysis paralysis. | Medium | CustomField entity model, JSONB serialization | Uses existing CustomField infrastructure (text, number, date, dropdown, currency, boolean). Recipe just provides initial set. |
| **Pre-configured workflow rules** | Industry recipes include basic automation (auto-assign leads, notify on stage change, schedule follow-ups). Saves setup time and establishes patterns. | Medium | WorkflowRule engine (triggers, conditions, actions) | Uses existing workflow model; recipe templates common rules per industry (e.g., auto dealership: assign test drive follow-up after 24h). |
| **Role seeding during provisioning** | At least Admin role pre-configured; optionally Sales Rep, Manager roles per industry. Tenants don't start with "no roles". | Low-Medium | Role/Permission entity model | v1.1 focuses on Admin. Granular permissions defer to v2. |
| **Blank/Custom option** | Tenants without matching industry need a starting point: either completely empty or minimal defaults (one pipeline stage, no fields, Admin role). Prevents wrong recipe selection regret. | Low | Recipe selection logic | Just means recipe dropdown has "Custom/Start from Scratch" option. Minimal seeding logic. |
| **Recipe metadata and descriptions** | Each recipe has human-readable name, icon, description (e.g., "Designed for car dealerships with typical sales stages"). Improves discoverability and reduces wrong-choice errors. | Trivial | Recipe registry/metadata | Helps users select correct recipe; builds confidence. |
| **Full post-provisioning customization** | All recipe-seeded data (stages, fields, rules, roles) are fully modifiable after provisioning. Recipe is a starting point, not a guardrail. This is critical to domain-agnostic positioning. | N/A | Existing config endpoints (LEAD-01/02/03, PIPE-04) | **Non-negotiable:** Tenants must be able to rename stages, delete fields, disable rules. No immutable recipe elements. |

---

## Admin UI Features (v1.2 Focus)

Admin pages for system configuration, tenant management, user/role management, and recipe administration. Backend APIs are fully built; UI is the focus.

### Admin UI Table Stakes

Features users expect in admin dashboards for multi-tenant SaaS systems.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| **Authentication (JWT + role-based route guards)** | Admins cannot access pages without login. Blazor `@authorize(Roles="...")` directives + JWT claims. | Low | IronMonkey JWT auth exists. Blazor Server uses AuthenticationStateProvider + cascading auth state. Per-page guards via `@attribute [Authorize(Roles="SuperAdmin")]`. |
| **Role-Based Access Control (RBAC)** | Different admin tiers (super-admin, tenant-admin, config-admin) need different UI sections. Super-admin sees all tenants; tenant-admin sees only their tenant; config-admin can edit config only. | Medium | JWT includes `tenant_id` and `roles` claims. Use `@authorize` attributes and `AuthorizeView` components with Roles parameter. Backend API enforces tenant scoping automatically. |
| **Navigation shell with sidebar** | Clear hierarchy of admin sections (Tenants, Users, Recipes, Config, Audit Log). Breadcrumbs for context. Responsive design (desktop/tablet). | Low | Tailwind CSS grid + Blazor layout. Fixed sidebar on desktop, collapsible on tablet. Standard pattern from SaaS templates. |
| **User management (CRUD)** | Create, list, edit, deactivate users within a tenant. Assign roles (Admin, Sales Rep, Manager). View user status (active/inactive), created date. | Low | CRUD tables + forms. Role assignment via dropdown or checkbox list. Validation via FluentValidation on backend. |
| **Tenant management view** | List all tenants with status, industry recipe applied, creation date, last activity. View tenant details: name, admin email, provisioned database. | Low | Read-only table for super-admin. May include approval workflow for signup requests (pending → approved → rejected). |
| **Audit logging view** | Queryable activity log: who did what, when, from where. Filter by actor, action type, date range. Display timestamp, actor email, action (create/update/delete), resource type, changes. | Medium | UI layer over existing ActivityLog. Must support pagination (large log tables), filtering, sorting. Real-time append (new entries appear without refresh) via SignalR. |
| **Form validation and error handling** | Field-level validation before submission. Clear error messages. Graceful API error handling with user-friendly messages. Disallow invalid state transitions. | Medium | FluentValidation on backend returns validation errors per field. UI must display `ValidationResult` details. Show toasts/alerts for errors. |
| **Logout and session management** | Users can log out. Session timeout with re-auth redirect. Clear auth token on logout. | Low | Standard Blazor Server lifecycle. `SignOut()` redirects to login page. Set JWT expiration and refresh logic. |
| **Tenant isolation enforcement** | Admin can only see/manage their own tenant's data. Super-admin sees all. No cross-tenant data leakage. API enforces scoping via `tenant_id` claim. | Medium | All API calls include tenant_id implicitly (via JWT claim). Blazor component is just a view; backend enforces isolation. Test thoroughly for leaks. |
| **Recipe management UI** | List recipes (name, industry, status: active/deactivate), create recipe, edit recipe fields/stages/rules, preview recipe, apply to new tenants. | Medium | CRUD pages for Recipe entity. Preview modal shows what recipe will seed (stages, fields, rules in JSON or tabular form). Deactivation prevents new tenants from selecting, but existing tenants keep their seeded data. |

### Admin UI Differentiators

Features that add value and set IronMonkey's admin experience apart.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| **Real-time data refresh via SignalR** | Admin sees live updates: new user approvals, new signup requests, config changes without manual refresh. SignalR pushes changes to all connected admins. | Medium | Blazor Server uses SignalR natively. Implement via `IHubContext<AdminHub>` in background services (Hangfire jobs). When domain event fires → outbox message → Hangfire job → hub.SendAsync("RecipeCreated", recipe) → component re-renders. |
| **Configuration wizards (multi-step forms)** | Complex setups (pipeline stages with 10+ fields, custom field definitions, routing rules) broken into 3-5 step flows with progress bar. Save-and-resume. | Medium | Multi-step Blazor components. Persist partial form state in SessionStorage or server-side. Validate per-step before advancing. Support back/forward navigation. |
| **Bulk operations** | Approve/reject multiple signup requests at once. Bulk role assignment to users. CSV export of audit logs. Bulk enable/disable workflow rules. | Medium-High | Select checkboxes in tables. Batch API endpoints for bulk actions. Show progress bar during processing. Confirm before bulk delete. |
| **Advanced dashboard analytics (super-admin only)** | Tenant health metrics: active users, storage usage, API calls, last login. System-wide trends: new signups per week, churn indicators. Resource usage heatmap. | High | Requires aggregation across all tenant databases. Direct LINQ feasible at v1 scale; pre-compute metrics for scale. Cache results (e.g., hourly). Consider materialized views for heavy queries. |
| **Activity timeline visualization** | Timeline view of admin actions instead of just table rows. Filter by actor, action type, date range. Drill down into specific changes. | Medium | Custom Blazor component rendering ActivityLog as timeline (vertical bar, event cards, timestamps). Leverage existing ActivityLog data. |
| **Recipe preview modal** | Before saving recipe, preview how it looks: stages in order, fields with types, rules with triggers. Side-by-side: template vs. customized version (if editing). | Low | Modal component with recipe JSON parsing. Render as tabular form (stages table, fields table, rules table). No new backend data needed. |
| **Bulk recipe application** | Apply same recipe to multiple new tenants in one operation instead of one-by-one. Select tenants → select recipe → apply. Async job with progress polling. | Low-Medium | Batch API endpoint accepting list of tenant IDs + recipe ID. UI form with tenant multi-select. Poll for async job completion. Show success/failure per tenant. |
| **Dark mode toggle** | User preference toggle for admin UI theme. Persisted in localStorage. WCAG 2.1 AA contrast compliance (4.5:1 minimum). | Low | Tailwind CSS dark mode (dark: prefix). Blazor CascadingParameter for theme preference. CSS media query `prefers-color-scheme`. |
| **Search & filter persistence** | Search/filter state preserved when leaving and returning to page. Restore filters from URL params or sessionStorage. | Low | Use query params (?search=foo&role=Admin). Populate form on component init from query params. |
| **Keyboard shortcuts for power users** | Ctrl+K for quick search/navigation, Ctrl+S to save forms, Ctrl+/ for help. | Low | Global keyboard event listener in root layout. Register shortcuts via JS interop or Blazor input events. |

### Admin UI Anti-Features

Features to explicitly NOT build in v1.2 admin UI.

| Anti-Feature | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| **Custom report builder for admins** | Complex query UI, filtering, export formats. Nice-to-have, not core admin function. Use BI tools instead. | Expose CSV export of audit logs, user lists, tenant metrics. Admins can import to Excel/Tableau for analysis. |
| **Real-time collaboration in config** | Multiple admins editing same recipe/tenant config simultaneously. Merge conflicts + undo/redo complexity. | Keep configs locked during edit (prevent concurrent admin edits). Last-write-wins for simplicity in v1.2. |
| **Mobile admin app** | Mobile-specific admin interface. Out of scope for v1. | Blazor Server responsive design sufficient for tablet. Phone access not required. Admin work is primarily desktop. |
| **Advanced permission granularity** | Per-field permissions, per-row access, custom permission rules. Premature for v1.2. | Role-based tiers (super-admin, tenant-admin, config-admin) sufficient. Defer granular permissions to v2+. |
| **Workflow rule visual builder** | Drag-drop rule creation UI. Sounds cool; often harder to use than form-based configuration. | Keep workflow rules as UI forms (if/then/action dropdowns). Users write rules via form, not visual node graph. |
| **Multi-language admin UI** | Supporting 10+ languages for admin panel. Scope creep. | English only for v1.2. i18n framework (using Resx) in place for future. |
| **Admin notification preferences** | Admins configuring email digests, alert thresholds. Scope creep. | Simple immediate notifications via audit log. Bulk digest as future feature. |
| **Undo/redo for admin actions** | Reverting bulk operations, config changes. Dangerous and complex. | Keep audit trail visible. Manual reversal via edit forms. Require confirmation for destructive actions. |
| **API documentation/explorer in admin UI** | REST API documentation embedded in admin panel. Out of scope. | Publish OpenAPI/Swagger docs separately (e.g., Swagger UI at /api/docs). Not admin-facing. |

---

## Blazor Server Admin UI Implementation Patterns

Based on multi-tenant Blazor Server best practices:

### Authentication Flow
1. **Login page** — Email + password form → validate against central DB → return JWT token.
2. **Token storage** — Store JWT in localStorage (or session if more secure).
3. **Blazor auth integration** — `AuthenticationStateProvider` extracts token and exposes claims.
4. **JWT claims** — Include `sub` (user ID), `email`, `tenant_id`, `roles` (comma-separated).
5. **Route protection** — `@attribute [Authorize(Roles = "SuperAdmin,TenantAdmin")]` on page components.
6. **Component protection** — `<AuthorizeView>` wrapper for conditional rendering.

### Real-Time Updates Pattern
- Blazor Server runs over SignalR (persistent connection built-in).
- For live admin dashboard: create `AdminHub` (SignalR hub).
- Background service (Hangfire job) publishes domain events → broadcasts via `IHubContext<AdminHub>`.
- Blazor component subscribes to hub and re-renders on data change.
- Example: User created → UserCreated domain event → outbox message → Hangfire job → IHubContext.Clients.All.SendAsync("UserCreated", user) → admin UI list refreshes in real-time.

### Multi-Step Form (Configuration Wizard) Pattern
```blazor
@page "/admin/recipes/create"
@using IronMonkey.ApiService.Recipes.Endpoints

<div class="form-wizard">
  <div class="steps-progress">
    <div class="step @(CurrentStep == 1 ? "active" : "")">1. Metadata</div>
    <div class="step @(CurrentStep == 2 ? "active" : "")">2. Pipeline Stages</div>
    <div class="step @(CurrentStep == 3 ? "active" : "")">3. Custom Fields</div>
    <div class="step @(CurrentStep == 4 ? "active" : "")">4. Workflow Rules</div>
    <div class="step @(CurrentStep == 5 ? "active" : "")">5. Review</div>
  </div>

  <div class="step-content">
    @if (CurrentStep == 1) {
      <RecipeMetadataStep @ref="step1" />
    } else if (CurrentStep == 2) {
      <PipelineStagesStep @ref="step2" />
    }
    <!-- etc. -->
  </div>

  <div class="form-actions">
    @if (CurrentStep > 1) {
      <button @onclick="GoBack">Back</button>
    }
    @if (CurrentStep < 5) {
      <button @onclick="GoNext" disabled="@!CurrentStepValid">Next</button>
    }
    @if (CurrentStep == 5) {
      <button @onclick="Save">Create Recipe</button>
    }
  </div>
</div>

@code {
  private int CurrentStep = 1;
  private RecipeMetadataStep step1;
  private PipelineStagesStep step2;
  // ...

  private bool CurrentStepValid => CurrentStep == 1 ? step1.IsValid() : /* ... */;

  private async Task GoNext() {
    if (!CurrentStepValid) return;
    CurrentStep++;
  }

  private void GoBack() => CurrentStep--;

  private async Task Save() {
    // Combine all steps' data and POST to API
  }
}
```

### Tenant Isolation in Blazor Components
All API calls must respect tenant scoping. Three approaches:
1. **JWT claim enforcement (recommended)** — Backend auto-scopes queries to JWT tenant_id claim. UI just calls `GET /api/admin/users` without specifying tenant.
2. **Query parameter** — `GET /api/admin/users?tenantId={tenantId}` (explicit but verbose).
3. **Header-based** — `httpClient.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId)` (good for shared HttpClient).

Recommended: Use JWT claim. Backend (via IUserContext injected in endpoints) auto-filters all queries.

---

## Feature Dependency Map (Admin UI + Lead Management)

```
Authentication & Authorization
  ├→ User Management UI
  ├→ Tenant Management UI
  ├→ Audit Logging UI
  └→ Role-Based Route Protection (all admin pages)

Recipe Management (backend already exists)
  ├→ Recipe Admin UI (CRUD pages)
  ├→ Recipe Preview Modal
  ├→ Bulk Recipe Application
  └→ Recipe-specific Audit Logs

System Configuration (backend already exists)
  ├→ Pipeline Stage Config UI
  ├→ Custom Field Definition UI
  ├→ Lead Routing Config UI
  ├→ Workflow Rule Management UI
  └→ Configuration Wizard (optional multi-step UX)

Audit Logging (backend infrastructure exists via interceptor)
  └→ Audit Log Query/Filter UI (read-only)

Real-Time Updates (Blazor Server + SignalR)
  ├→ Live user list refresh
  ├→ Live approval request notifications
  └→ Live config change notifications (background services → SignalR → components)

Advanced Analytics (super-admin only)
  └→ Requires metric aggregation + caching
```

All backend APIs exist. Admin UI is purely presentation layer connecting to existing endpoints.

---

## MVP Recommendation for v1.2 (Admin UI)

### Phase 1: Foundation & Core Admin Functions (Weeks 1-2)
1. **Login page + JWT auth** — Email/password form, token storage, auth guards (blocking feature for all other pages)
2. **Navigation shell** — Sidebar + header, Tailwind CSS styling, responsive layout
3. **User management** — List, create, edit, deactivate users; role assignment
4. **Tenant management** — List tenants, view details, approval workflow (pending → approved)
5. **Audit logging view** — Read-only table of ActivityLog with filtering/sorting
6. **Role-based route protection** — `@authorize(Roles="...")` on pages + component-level `<AuthorizeView>`

### Phase 2: Configuration & Recipe Admin (Weeks 3-4)
1. **Recipe management** — List, create, edit, deactivate; preview modal
2. **Pipeline stage configuration** — CRUD UI for stages within tenant
3. **Custom field definitions** — CRUD UI for custom fields (type, validation, required/optional)
4. **Lead routing configuration** — CRUD UI for routing rules (conditions, assignments)
5. **Workflow rule management** — CRUD UI for workflow rules (triggers, conditions, actions)

### Phase 3: Differentiators (Weeks 5-6, if time allows)
1. **Real-time data refresh** — SignalR integration for live user list, approval requests
2. **Configuration wizards** — Multi-step forms for complex setups (optional, nice-to-have)
3. **Bulk operations** — Approve multiple requests, bulk role assignment
4. **Dark mode toggle** — Simple theme preference (Tailwind dark mode)

### Defer to v1.3+
- Advanced dashboard analytics (requires metric aggregation)
- Activity timeline visualization (nice-to-have)
- Search/filter persistence (nice-to-have)
- Keyboard shortcuts (power-user feature)
- Bulk recipe application (v1.2 Phase 3 if time)

---

## Complexity Breakdown (Admin UI)

| Feature Group | Estimated Effort | Risk | Dependencies |
|---------------|------------------|------|--------------|
| Auth + Navigation | 3-5 days | Low | JWT auth (exists), Blazor Server layout |
| User Management | 2-3 days | Low | CRUD API (exists), FluentValidation (exists) |
| Tenant Management | 3-4 days | Low | Tenant API (exists) |
| Audit Logging UI | 3-4 days | Low | ActivityLog data (exists), query API (exists) |
| Recipe Management | 4-5 days | Medium | Recipe API (exists), preview schema understanding |
| Pipeline/Field/Routing/Workflow Config UIs | 5-7 days each | Medium | Config APIs (exist), validation |
| Real-Time Updates (SignalR) | 4-5 days | Medium | SignalR setup, background services (Hangfire exists) |
| Configuration Wizards | 4-6 days | Medium | Multi-step component scaffolding |
| Dark Mode Toggle | 1-2 days | Low | Tailwind dark mode, CSS variable management |
| Advanced Analytics | 7-10 days | High | Metric aggregation, query optimization, caching |

**Critical path:** Auth (5d) → User Mgmt (3d) → Tenant Mgmt (4d) → Config UIs (7d each) = ~19-26 days minimum for core features.

---

## Industry Recipe Details (Automobile Dealership)

**MVP target:** Pre-configured setup for high-volume auto sales operations.

**Why this industry:** High-velocity pipeline, structured "Road-to-the-Sale" process, well-defined stages, regulatory/compliance drivers (title, insurance, payment). Strong market demand (Pipedrive, HubSpot, LeadsBridge specialize in auto dealership CRM). Clear competitive advantage for IronMonkey if recipes onboard dealerships in 1 day vs Salesforce in 2 weeks.

**Pipeline stages:**
1. **Lead** — Initial inquiry, lead captured
2. **Contact Attempt** — Sales rep reached out
3. **Appointment Set** — Lead committed to showroom visit
4. **Test Drive** — Lead test drove vehicle
5. **Negotiation** — Pricing/terms discussion
6. **Sold** — Deal closed
7. **Lost** — Lead disqualified

Rationale: Matches standard dealership "Road-to-the-Sale" funnel documented by dealership training programs.

**Key custom fields:**
- **Vehicle Interest** (Dropdown) — Make/model options (Toyota, Ford, Chevy, etc.)
- **Budget** (Currency) — Price range the lead is considering
- **Trade-in Value** (Currency) — Value of existing vehicle if applicable
- **Financing Needed** (Boolean) — Yes/No for financing requirement
- **Insurance Provider** (Text) — Current insurance company
- **Test Drive Completed** (Date) — When test drive occurred
- **Finance Source** (Dropdown) — Options: Dealer financed, Bank, Lease, Cash

**Workflow rule examples:**
- **Auto-assign new leads** via round-robin to available sales reps
- **Email notification** when lead moves to "Test Drive" stage (confirm appointment, send test drive expectations)
- **Schedule follow-up task** 24h after test drive if lead not in "Sold" stage (check-in call)
- **Escalate to manager** if lead stalled in "Negotiation" > 7 days (unblock deal)
- **Auto-archive** leads in "Lost" stage after 90 days

**Roles seeded:** Admin, Sales Rep, Sales Manager (basic—no granular permissions in v1.1)

**Complexity:** Medium. Uses existing infrastructure (CustomField types, WorkflowRule, Role). Primary effort is domain knowledge (which stages, which fields, which rules).

**Confidence:** HIGH — Research verified against Driftrock, Pipedrive, HubSpot auto CRM documentation.

---

## Industry Recipe Details (Educational Institution)

**MVP target:** Pre-configured setup for higher ed admissions and enrollment workflows.

**Why this industry:** Distinct pipeline from B2B sales (inquiry → application → interview → enrollment), high seasonality (academic calendar), strong regulatory drivers (accreditation, data privacy). Growing CRM market (Element451, Meritto, Creatrix, Classe365 specialize in ed CRM). Clear use case for IronMonkey.

**Pipeline stages:**
1. **Lead/Inquiry** — Student expressed interest
2. **Application Submitted** — Student submitted application
3. **Under Review** — Admissions reviewing application
4. **Interview Scheduled** — Interview scheduled
5. **Interviewed** — Interview completed
6. **Offer Extended** — Offer sent to student
7. **Enrolled** — Student accepted offer and enrolled
8. **Rejected** — Student not accepted

Rationale: Standard higher ed admissions funnel documented by NACAC (National Association for College Admission Counseling) best practices.

**Key custom fields:**
- **Degree Program** (Dropdown) — Business, Engineering, Nursing, Liberal Arts, etc.
- **Test Scores** (Text) — SAT/ACT scores
- **Current Education Level** (Dropdown) — High School, Bachelor's, Master's
- **Application Status** (Dropdown) — Pending, Submitted, Reviewed, Complete
- **Interview Date** (Date) — Scheduled interview date
- **Offer Made** (Boolean) — Has offer been extended?
- **Expected Enrollment Term** (Dropdown) — Spring, Fall (next year)
- **International Student** (Boolean) — Requires visa sponsorship?

**Workflow rule examples:**
- **Email notification** when application status changes (updates parents/student)
- **Schedule interview** 5 business days after application received (consistent timeline)
- **Escalate to admissions director** if lead > 30 days in "Under Review" (process bottleneck)
- **Auto-assign** to admissions counselor based on degree program (routing)
- **Email offer letter** when offer extended automatically (time-to-value)
- **Archive enrolled students** into separate view (cleanup)

**Roles seeded:** Admin, Admissions Counselor, Admissions Director

**Complexity:** Medium. Same as auto dealership—uses existing infrastructure, primary effort is domain knowledge.

**Confidence:** HIGH — Research verified against Element451, Zoho Education CRM, LeadSquared Higher Ed documentation.

---

## Industry Recipe Details (Blank/Custom)

**Minimal starting point for tenants without a matched industry.**

**Includes:**
- Single default pipeline stage: "Lead"
- No pre-configured custom fields (tenant must add as needed)
- No workflow rules
- Admin role only

**Rationale:** Prevents "picked the wrong recipe" regret. Avoids false constraints. Users in real estate, insurance, professional services, niche industries can build from minimal foundation.

**Complexity:** Trivial — near-zero seeding logic.

---

## Recipe Anti-Patterns (What NOT to Build)

| Anti-Pattern | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| **Locked/immutable recipe elements** | Tempting to "protect" recipe defaults by making stages/fields read-only, but contradicts domain-agnostic value proposition and user expectations. | Allow ALL seeded data to be customized post-provisioning. Recipe is a starting point, not a guardrail. This is critical to product positioning. |
| **Proprietary recipe format or custom DSL** | Inventing custom recipe JSON schema or domain-specific language for templates adds complexity, makes maintenance hard, and locks in implementation. | Use existing IronMonkey entity models (PipelineStage, CustomField, WorkflowRule, Role). Recipe = seeder code or SQL migrations that populate these tables. Keep recipes as first-class entities in the domain. |
| **Recipe marketplace or community-contributed templates** | Out of scope for v1.1 and adds governance, liability, and support burden. "Certified recipes" vs "community recipes" requires moderation. | Stick to 2-3 hardcoded recipes for v1.1 (Auto, Education, Blank). If future demand emerges and user base matures, revisit marketplace approach. |
| **Forced recipe upsell ("Pro recipes")** | Recipes are part of core product, not a paid upgrade tier. Contradicts positioning of equal-access domain-agnostic platform. | All recipes available to all tenants regardless of plan. No artificial tiers. |
| **Automatic enforcement of recipe best practices** | Tempting to "prevent deletion" of recipe stages or "lock" fields to enforce best practices, but breaks customization contract. | Once seeded, recipe data is fully under tenant control. No enforcement. Tenants own their config. |
| **Recipe versioning and auto-upgrade** | Complex and risky: if recipe improves, should existing tenants upgrade? Merging recipe updates with tenant customizations is error-prone. | Recipes are immutable at v1.1. Recipes are point-in-time templates. Tenants can manually re-seed from newer recipes if they want, but no auto-upgrade. |

---

## Feature Dependencies (Overall)

Map of which features unlock others:

```
Multi-tenancy
  ├→ Configurable fields (each tenant has different schema)
  ├→ Custom roles and permissions (each tenant has different org structure)
  ├→ Industry recipe templates (reduce time-to-value for onboarding)
  └→ Per-tenant data isolation (compliance requirement)

Industry recipes (NEW for v1.1)
  ├→ Pre-configured pipeline stages
  ├→ Pre-configured custom fields
  ├→ Pre-configured workflow rules
  └→ Reduces time-to-first-lead and improves activation

Admin UI (NEW for v1.2)
  ├→ Recipe Management (admin can CRUD recipes in UI)
  ├→ Tenant Management (admin can approve/manage tenants)
  ├→ User Management (admin can manage users within tenant)
  ├→ System Config UI (pipeline, fields, routing, workflows)
  └→ Audit Logging (see all admin actions)

Lead records + custom fields
  ├→ Pipeline stages (organize leads by status)
  ├→ Lead scoring (prioritize within pipeline)
  ├→ Task/follow-up management (next actions per lead)
  └→ Activity timeline (full lead history)

Email integration
  ├→ Multi-channel communication (add SMS, WhatsApp)
  ├→ Workflow automation (trigger emails on lead status)
  └→ Bulk communication (email campaigns)

Lead assignment + routing
  ├→ Task/follow-up management (tasks go to assigned person)
  ├→ Real-time notifications (alert assignee of new lead)
  └→ Team management (routing rules reference team structure)

Workflow automation
  ├→ Lead scoring (automation triggers on score change)
  ├→ Lead assignment (auto-assign based on rules)
  ├→ Task creation (create follow-up on trigger)
  └→ Notifications (alert on trigger)

Dashboards and reporting
  ├→ Lead scoring (conversion by lead quality)
  ├→ Activity timeline (team productivity metrics)
  ├→ Email tracking (open/click rates)
  └→ All lead fields (dashboard filters must reflect tenant schema)

Lead capture (all ingestion modes)
  ├→ Lead deduplication (avoid duplicate records)
  ├→ Lead enrichment (enhance captured data)
  └→ Audit logging (track where leads came from)
```

---

## MVP Recommendation (Overall)

**Prioritize for Phase 1 (Core Lead Management):**

1. **Lead records with configurable custom fields** — Core data model. Nothing else works without this.
2. **Pipeline visualization with custom stages** — Users must see their pipeline. Non-negotiable.
3. **Task and follow-up management** — Prevents leads falling through cracks.
4. **Lead assignment (manual + basic routing)** — Gets leads to the right person.
5. **Email integration** — Most common communication channel. Critical for adoption.
6. **SMS capability** — Required per PROJECT.md for omnichannel.
7. **Basic dashboards** — Pipeline overview, conversion rates, agent metrics.
8. **Multi-tenant data isolation** — Must ship secure from day 1.
9. **Manual lead entry and CSV bulk import** — Minimum viable ingestion.
10. **Audit logging** — Compliance and trust foundation.

**Add for v1.1 (Industry Recipes):**

11. **Industry recipe selection at signup** — Improves onboarding time-to-value, activation rates, reduces churn
12. **Automobile & Education recipes** — Two high-demand industries with proven demand signals
13. **Blank/Custom recipe option** — Escape hatch for unmatched industries
14. **Recipe seeding during provisioning** — Automates configuration for recipe users

**Add for v1.2 (Admin UI):**

15. **Login + JWT auth + role-based route guards** — Foundation for all admin pages
16. **Navigation shell** — Sidebar + header with admin sections
17. **User management UI** — CRUD for users, role assignment
18. **Tenant management UI** — List tenants, approval workflow
19. **Audit logging UI** — Query/filter ActivityLog
20. **Recipe management UI** — CRUD recipes, preview, deactivate
21. **Configuration UIs** — Pipeline stages, custom fields, routing, workflows
22. **Real-time updates (SignalR)** — Live data refresh (optional; Phase 3 if time)

**Defer to Phase 2+ (Post-v1.2):**

- **Advanced lead scoring and AI** (requires data quality foundation first)
- **Web form lead capture** (manual entry sufficient to start, high dependency on onboarding flow)
- **Workflow automation with conditional triggers** (rule-based scoring and assignment can ship in Phase 1; advanced workflows in Phase 2)
- **Phone integration and click-to-dial** (VoIP adds infrastructure complexity, SMS covers most urgent needs)
- **Custom report builder** (basic dashboards are sufficient, custom reports are nice-to-have)
- **Email/calendar sync and Outlook plugin** (nice-to-have, low adoption for MVP)
- **Advanced lead enrichment** (foundational but post-MVP; manual enrichment possible via API)
- **Mobile native app** (web responsiveness sufficient, PWA possible later)
- **Recipe versioning, upgrades, marketplace** (too ambitious for v1.1; revisit if demand signal emerges)
- **Advanced admin analytics** (requires aggregation; v1.2 Phase 3 if time)
- **Configuration wizards** (v1.2 Phase 3 if time; form-based config sufficient for MVP)

---

## Critical Feature Dependencies for Phase Ordering

1. **Lead records + custom fields must ship before anything else** — Everything depends on having the data model right.
2. **Multi-tenant isolation must be baked in from Phase 1** — Can't retrofit security later.
3. **Email integration should ship in Phase 1** — Early proof of omnichannel value.
4. **Pipeline + task management together unlock usage** — Rep productivity depends on both.
5. **Dashboard reporting should be quick-follow in Phase 1** — Managers need visibility to trust the system.
6. **Workflow automation and scoring can wait for Phase 2** — Manual workflows are viable for MVP; automation amplifies them.
7. **Industry recipes in v1.1 unblock SMB onboarding** — Key differentiator vs Salesforce (weeks) and HubSpot (days).
8. **Admin UI in v1.2 unblocks system configurability** — Tenants can fully self-serve after recipes; super-admin can manage platform.

---

## Sources

### General CRM and Onboarding Best Practices
- [Pipedrive CRM Onboarding Best Practices](https://www.pipedrive.com/en/blog/crm-onboarding)
- [SaaS Onboarding Flows That Convert in 2026](https://designrevision.com/blog/saas-onboarding-best-practices)
- [Rocketlane: Best Customer Onboarding Tools for 2026](https://www.rocketlane.com/blogs/customer-onboarding-tools)
- [SaaS Lens: Tenant Onboarding (AWS)](https://docs.aws.amazon.com/wellarchitected/latest/saas-lens/tenant-onboarding.html)
- [7 User Onboarding Best Practices for 2026](https://formbricks.com/blog/user-onboarding-best-practices)
- [Client Onboarding Process 2026: Improve Retention with Templates & Tools](https://martal.ca/client-onboarding-lb/)

### Multi-Tenant SaaS Admin Patterns
- [Multi-Tenant Deployment: 2026 Complete Guide & Examples | Qrvey](https://qrvey.com/blog/multi-tenant-deployment/)
- [18 Best SaaS Admin Dashboard Templates 2026 - AdminLTE.IO](https://adminlte.io/blog/saas-admin-dashboard-templates/)
- [SaaS Multitenancy: Components, Pros and Cons and 5 Best Practices | Frontegg](https://frontegg.com/blog/saas-multitenancy)
- [How to Create a Good Admin Panel: Design Tips & Features List | Aspirity](https://aspirity.com/good-admin-panel-design)
- [Audit Logging Best Practices, Components & Challenges | Sonar](https://www.sonarsource.com/resources/library/audit-logging/)

### Blazor Server Multi-Tenant Admin Patterns
- [Implementing Authorization in Blazor Server .NET 7 - Blazor School](https://blazorschool.com/tutorial/blazor-server/dotnet7/implementing-authorization-519268)
- [ASP.NET Core Blazor authentication and authorization | Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/?view=aspnetcore-10.0)
- [Role-Based Access Control (RBAC) in Blazor Applications](https://auth0.com/blog/role-based-access-control-in-blazor-apps/)
- [MultiTenancy — Blazor Boilerplate 2.0.0 Dokumentation](https://blazor-boilerplate.readthedocs.io/de/latest/features/multitenancy.html)
- [BlazorPlate Features - Multi-Tenant & SaaS Template](https://www.blazorplate.net/features)

### Real-Time UI Updates with Blazor Server + SignalR
- [Real-Time Blazor Apps: Integrating SignalR and Blazorise Notifications](https://blazorise.com/blog/real-time-blazor-apps-signalr-and-blazorise-notifications/)
- [Use ASP.NET Core SignalR with Blazor | Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/tutorials/signalr-blazor?view=aspnetcore-10.0)

### Form Wizards & Multi-Step UX
- [Wizard UI Pattern: When to Use It and How to Get It Right](https://www.eleken.co/blog-posts/wizard-ui-pattern-explained)
- [3 Multi-Step Form Best Practices](https://www.formassembly.com/blog/multi-step-form-best-practices/)

### CRM Comparison and Market Research
- [Compare Zoho CRM vs HubSpot features and pricing](https://www.zoho.com/crm/compare/hubspot.html)
- [Salesforce vs Zoho vs HubSpot vs Pipedrive – The Best CRM for 2026](https://blog.salesflare.com/compare-salesforce-zoho-hubspot-pipedrive)
- [HubSpot CRM vs Zoho CRM: Features and Cost Comparison 2026](https://www.capterra.com/compare/152373-155928/HubSpot-CRM-vs-Zoho-CRM)
- [Salesforce vs HubSpot vs Pipedrive: CRM Comparison for Sales Teams (2026)](https://www.sybill.ai/blogs/salesforce-vs-hubspot-vs-pipedrive)
- [Pipedrive vs HubSpot: Complete CRM Comparison Guide for 2026](https://nuacom.com/pipedrive-vs-hubspot-complete-crm-comparison-guide/)

### Automotive Dealership CRM
- [Automotive CRM Software: Best Platforms, Key Features & How to Choose](https://www.driftrock.com/blog/automotive-crm-software)
- [Pipedrive Automotive CRM](https://www.pipedrive.com/en/industries/automotive-crm)
- [The Complete Guide to Automotive CRM in 2025](https://leadsbridge.com/blog/automotive-crm/)
- [Best Automotive CRM Software: Auto Dealer CRM Systems](https://crm.org/crmland/automobile-crm)
- [HubSpot Maximize Car Sales with an Automotive CRM](https://www.hubspot.com/products/crm/automotive)

### Education and Higher Ed CRM
- [9 Best Admissions CRM Tools for Education in 2025](https://www.superleap.com/blog/crm/education)
- [Pipedrive Higher Education CRM](https://www.pipedrive.com/en/industries/higher-education-crm)
- [Zoho CRM for Education](https://www.zoho.com/crm/verticals/education/)
- [Best 15 CRMs for Student Admissions and Recruitment](https://goedmo.com/blog/best-15-crms-for-student-admissions-and-recruitment/)
- [LeadSquared AI-Powered Admission CRM For Higher Education](https://www.leadsquared.us/higher-education-crm/)

### Multi-Tenant Architecture and Configuration
- [Multi-Tenant Database Architecture Patterns Explained](https://www.bytebase.com/blog/multi-tenant-database-architecture-patterns-explained/)
- [Multitenant SaaS Patterns (Azure SQL Database - Microsoft Learn)](https://learn.microsoft.com/en-us/azure/azure-sql/database/saas-tenancy-app-design-patterns?view=azuresql)
- [How to Design a Multi-Tenant SaaS Architecture](https://clerk.com/blog/how-to-design-multitenant-saas-architecture)
- [The Developer's Guide to SaaS Multi-Tenant Architecture (WorkOS)](https://workos.com/blog/developers-guide-saas-multi-tenant-architecture)
- [Ultimate Guide to Multi-Tenant SaaS Data Modeling](https://www.flightcontrol.dev/blog/ultimate-guide-to-multi-tenant-saas-data-modeling)

### CRM Implementation and Configuration
- [CRM Implementation Checklist 2026](https://maciejturek.com/resources/crm-implementation-checklist-2026.html)
- [Step-by-Step CRM Development Process for 2026](https://sisgain.com/blogs/crm-development)
- [Enterprise HubSpot CRM Architecture Best Practices](https://www.campaigncreators.com/blog/enterprise-hubspot-crm-architecture-best-practices)
- [Building a Cloud-Based Lead Management CRM System with Container Architecture](https://abcloudz.com/blog/building-a-cloud-based-lead-management-crm-system-with-container-architecture/)

### Additional CRM Features and Trends
- [What Is a Sales Pipeline Tool? Features, Benefits, ROI](https://www.apollo.io/insights/sales-pipeline-tool)
- [What Is Pipeline Management? Examples & Best Practices [2026]](https://monday.com/blog/crm-and-sales/pipeline-management/)
- [CRM Dashboards in 2026: The Essential KPIs and Real-World Examples](https://monday.com/blog/crm-and-sales/crm-dashboards/)
- [What Is CRM Reporting? How to Use It to Optimize Sales in 2026 and Beyond](https://www.breakcold.com/blog/crm-reporting)
- [Best Lead Enrichment Tools for 2026](https://pipeline.zoominfo.com/sales/lead-enrichment-tools)
- [Best CRM for WhatsApp 2026: 10 WhatsApp Business Integrations](https://crm.org/news/best-whatsapp-crm)
- [9 Best CRM SMS Integration Services in 2026](https://mobile-text-alerts.com/articles/best-crm-sms-integration-services)
