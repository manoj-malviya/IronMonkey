# Phase 11: Tenant Management UI - Context

**Gathered:** 2026-04-01
**Status:** Ready for planning

<domain>
## Phase Boundary

Build admin pages for tenant monitoring and signup request management: a tabbed page at `/admin/tenants` with a tenant overview list and a signup requests list. Admins can view all tenants, review pending signup request details (expandable row), approve requests (auto-triggering provisioning), and reject requests with a reason. All pages live under `/admin/tenants` within the authenticated admin shell from Phase 9.

</domain>

<decisions>
## Implementation Decisions

### Page Organization
- **D-01:** Single page at `/admin/tenants` with two tabs: "Tenants" and "Signup Requests"
- **D-02:** Tab switching uses client-side state (no navigation) — follows RecipePreview tabbed pattern from Phase 10
- **D-03:** Default tab is "Tenants" (the operational overview)

### Tenant List Tab (TNUI-01)
- **D-04:** Data table with columns: Name, Status, Subscription Plan, Provisioned (yes/no badge), Applied Recipe, Created Date
- **D-05:** Status displayed as colored badge — green for Active, gray for other states
- **D-06:** Provisioned column: green checkmark for yes, gray dash for no
- **D-07:** Empty state: centered message "No tenants yet — approve signup requests to create tenants"
- **D-08:** Read-only view — no actions on tenants in this phase (management deferred)

### Signup Requests Tab (TNUI-02, TNUI-03, TNUI-04)
- **D-09:** Data table with columns: Company Name, Admin Email, Status (badge), Created Date, Actions
- **D-10:** Status filter toggle at top — default shows "Pending" requests, option to show all
- **D-11:** Expandable row to view full request details: Company Name, Admin Email, Phone, Company Size, Address, Billing Contact, Recipe ID, Review Note
- **D-12:** Actions column: "Approve" and "Reject" buttons shown only for Pending requests
- **D-13:** Empty state for no pending: "No pending signup requests"

### Approve/Reject Flow
- **D-14:** Approve action: confirmation modal with optional approval note text field, then auto-triggers provisioning (chains POST /admin/signup/{id}/approve → POST /admin/tenants/{id}/provision)
- **D-15:** Reject action: confirmation modal with required rejection reason text field
- **D-16:** After successful approve+provision or reject, row updates in-place (optimistic UI) and success toast/notification displayed
- **D-17:** If provisioning fails after approval, show error message but signup status remains Approved — admin can retry via a "Provision" button that appears on Approved-but-not-provisioned entries

### Claude's Discretion
- Exact Tailwind styling for table rows, badges, tabs (maintain consistency with Phase 9/10 palette)
- Loading skeleton/spinner approach while API calls complete
- Toast/notification style for success/error messages
- Tab component implementation details (CSS-only vs Blazor component)
- Exact spacing and layout proportions
- Whether expandable row uses animation/transition

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Backend Tenant/Signup API Endpoints
- `IronMonkey.ApiService/Authentication/Endpoints/ListSignupRequestsEndpoint.cs` — GET `/admin/signup?status=`, returns list of SignupRequestSummary (Id, CompanyName, AdminEmail, Status, CreatedAt)
- `IronMonkey.ApiService/Authentication/Endpoints/ApproveTenantEndpoint.cs` — POST `/admin/signup/{id}/approve`, body: { ApprovalNote? }, requires Pending status
- `IronMonkey.ApiService/Authentication/Endpoints/RejectTenantEndpoint.cs` — POST `/admin/signup/{id}/reject`, body: { RejectionReason? }, requires Pending status
- `IronMonkey.ApiService/Authentication/Endpoints/ProvisionTenantEndpoint.cs` — POST `/admin/tenants/{id}/provision`, creates tenant DB + seeds data
- `IronMonkey.ApiService/Endpoints.cs` — MapPlatformAdminEndpoints (lines 74-85) registers all admin endpoints under `/admin` group with RequireAuthorization

### Data Model
- `IronMonkey.Data/Entities/SignupRequest.cs` — Fields: CompanyName, AdminEmail, Phone, RecipeId, CompanySize, Address, BillingContact, Status (Pending/Approved/Rejected), ReviewNote, TenantId
- `IronMonkey.Data/Entities/Tenant.cs` — Fields: Name, Slug, SubscriptionPlan, Status, IsProvisioned, ApprovalStatus, ApprovalNote, ApprovedAt, ProvisionedAt, AppliedRecipeId, AppliedRecipeVersion, DatabaseConnectionString

### Phase 9/10 UI Patterns (must follow)
- `IronMonkey.Web/Components/Pages/Admin/Recipes/RecipeList.razor` — Data table pattern with status filter, action buttons, empty state, loading state
- `IronMonkey.Web/Components/Pages/Admin/Recipes/RecipePreview.razor` — Tabbed view pattern (client-side tab switching)
- `IronMonkey.Web/Components/Pages/Login.razor` — Form validation pattern, loading state, error handling
- `IronMonkey.Web/Components/Layout/AdminSidebar.razor` — Nav structure (tenants link at `/admin/tenants`)
- `IronMonkey.Web/HttpHandlers/BearerTokenHandler.cs` — Auto-injects Bearer token on AdminApi calls

### Project Configuration
- `.planning/REQUIREMENTS.md` — TNUI-01 through TNUI-04 (this phase's requirements)
- `.planning/PROJECT.md` — Key decisions: native Razor + Tailwind only, no component libraries

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `BearerTokenHandler` + named HttpClient `"AdminApi"` — all API calls from Blazor pages use this; inject `IHttpClientFactory` and create `"AdminApi"` client
- RecipeList.razor data table pattern — status badges, action buttons, loading/empty states (direct reuse pattern)
- RecipePreview.razor tabbed view — client-side tab switching with active tab styling (direct reuse pattern)
- Login.razor validation pattern — `EditForm` with `DataAnnotationsValidator` and inline `ValidationMessage` components

### Established Patterns
- **API calls**: Use `IHttpClientFactory.CreateClient("AdminApi")` → `GetFromJsonAsync<T>` / `PostAsJsonAsync` / `DeleteAsync`
- **Page structure**: `@page "/admin/..."` + `@attribute [Authorize]` + `@inject IHttpClientFactory HttpClientFactory`
- **Styling**: Tailwind utility classes, slate/indigo color scheme, Heroicons for icons
- **Status badges**: Green for active/approved, gray for inactive/other (established in RecipeList)

### Integration Points
- ListSignupRequests returns: Id, CompanyName, AdminEmail, Status, CreatedAt — but NOT full detail fields (Phone, CompanySize, Address, BillingContact, RecipeId)
- **Gap: No "list tenants" endpoint exists** — TNUI-01 needs a tenant list from CentralDbContext. A new GET endpoint (e.g., GET /admin/tenants) must be created as part of this phase
- **Gap: No "get signup request detail" endpoint** — ListSignupRequests returns summary only. For expandable row details, either enhance the list endpoint to include all fields, or add a GET /admin/signup/{id} detail endpoint
- Approve endpoint returns success message only — UI must refresh list after action
- AdminSidebar already has a "Tenants" link — routing is ready

</code_context>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches. Key constraints:
1. Two new backend endpoints needed (list tenants, signup request detail) — keep them minimal following existing patterns
2. Approve flow chains two API calls (approve + provision) — handle partial failure gracefully
3. Follow Phase 10 visual patterns closely for consistency

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 11-tenant-management-ui*
*Context gathered: 2026-04-01*
