# Phase 12: User & Role Management UI - Context

**Gathered:** 2026-04-01
**Status:** Ready for planning

<domain>
## Phase Boundary

Build admin pages for user account management within a tenant: list all users in a data table at `/admin/users`, create new users via a dedicated form page with auto-generated password, edit existing users on a dedicated edit page with password reset capability, and deactivate users via confirmation modal. Role assignment uses a dropdown from existing roles. All pages live under `/admin/users` within the authenticated admin shell from Phase 9.

</domain>

<decisions>
## Implementation Decisions

### User List Page
- **D-01:** Dedicated page at `/admin/users` following RecipeList data table pattern
- **D-02:** Table columns: Name, Email, Role, Status (badge), Created Date
- **D-03:** Status displayed as colored badge — green for Active, gray for Deactivated
- **D-04:** Toggle checkbox "Show Deactivated Users" (hidden by default) — same pattern as RecipeList's "Show Deactivated Recipes"
- **D-05:** Empty state: centered message with "Create your first user" CTA button
- **D-06:** Action buttons per row: Edit, Deactivate (Deactivate only shown for active users)

### Create User Page (USUI-02)
- **D-07:** Dedicated page at `/admin/users/create` following RecipeCreate pattern
- **D-08:** Form fields: Name, Email, Role (dropdown from ListRoles endpoint)
- **D-09:** Password auto-generated on server side — displayed once to admin after successful creation in a success banner/modal with copy-to-clipboard
- **D-10:** No password field in the form — admin does not enter a password
- **D-11:** Inline validation matching Phase 9 pattern — errors below each field + summary banner at top
- **D-12:** Single "Create User" button at bottom — saves via POST to create user endpoint

### Edit User Page (USUI-03)
- **D-13:** Dedicated page at `/admin/users/{id}/edit` following RecipeEdit pattern
- **D-14:** Pre-populated form with Name, Email, Role dropdown — no password field in main form
- **D-15:** Separate "Reset Password" button — generates new password and displays it once (same display pattern as create)
- **D-16:** Single "Save" button for name/email/role changes
- **D-17:** "Back to List" navigation at top

### Deactivate User (USUI-04)
- **D-18:** Confirmation modal triggered from list page action button — shows user name for confirmation
- **D-19:** Sets IsDeleted=true (soft delete via BaseTenantEntity global query filter)
- **D-20:** Row updates in-place after successful deactivation (optimistic UI)
- **D-21:** No session invalidation in this phase — deactivated user's existing JWT remains valid until expiry (noted limitation)

### Claude's Discretion
- Exact Tailwind styling for table rows, badges, form inputs (maintain consistency with Phase 9/10/11 palette)
- Loading skeleton/spinner approach while API calls complete
- Toast/notification style for success messages after save/deactivate
- Password display UX (modal vs inline banner, copy button implementation)
- Role dropdown styling and empty state if no roles exist

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Backend User/Role API Endpoints
- `IronMonkey.ApiService/Authentication/Endpoints/CreateUser.cs` — POST `/users`, body: { TenantId, Name, Email, Password, RoleId }. NOTE: Currently takes plaintext password and stores as-is — phase must hash with BCrypt before saving
- `IronMonkey.ApiService/Authentication/Endpoints/ListRoles.cs` — GET `/user-management/roles`, returns list of (RoleId, RoleName)
- `IronMonkey.ApiService/Endpoints.cs` — MapUserEndpoints (line 87-94) and MapUserManagementEndpoints (line 105-116)

### Data Model
- `IronMonkey.Data/Entities/User.cs` — Fields: Name, Email, Password, IdentityId, Roles (many-to-many). Factory: User.Create(tenantId, name, email, password, role). Inherits BaseTenantEntity (TenantId, IsDeleted, CreatedAt, UpdatedAt)
- `IronMonkey.Data/Entities/Role.cs` — Static roles: SuperAdmin(1), Admin(201), Owner(301), TeleCaller(302). Has Name, Users, Permissions collections
- `IronMonkey.Data/TenantDbContext.cs` — User and Role are in tenant DB (not central). Global query filter enforces TenantId and !IsDeleted

### Phase 9/10/11 UI Patterns (must follow)
- `IronMonkey.Web/Components/Pages/Admin/Recipes/RecipeList.razor` — Data table pattern with status filter, action buttons, empty state
- `IronMonkey.Web/Components/Pages/Admin/Recipes/RecipeCreate.razor` — Create form pattern with validation
- `IronMonkey.Web/Components/Pages/Admin/Recipes/RecipeEdit.razor` — Edit form pattern with pre-population
- `IronMonkey.Web/Components/Pages/Admin/Tenants/TenantManagement.razor` — Modal confirmation pattern (approve/reject)
- `IronMonkey.Web/HttpHandlers/BearerTokenHandler.cs` — Auto-injects Bearer token on AdminApi calls

### Project Configuration
- `.planning/REQUIREMENTS.md` — USUI-01 through USUI-04 (this phase's requirements)
- `.planning/PROJECT.md` — Key decisions: native Razor + Tailwind only, no component libraries

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `BearerTokenHandler` + named HttpClient `"AdminApi"` — all API calls from Blazor pages use this
- RecipeList.razor data table pattern — status badges, action buttons, loading/empty states, deactivation toggle
- RecipeCreate/RecipeEdit form patterns — EditForm, DataAnnotationsValidator, ValidationMessage
- TenantManagement.razor modal pattern — confirmation dialogs with action buttons

### Established Patterns
- **API calls**: Use `IHttpClientFactory.CreateClient("AdminApi")` → `GetFromJsonAsync<T>` / `PostAsJsonAsync` / `PutAsJsonAsync` / `DeleteAsync`
- **Page structure**: `@page "/admin/..."` + `@attribute [Authorize]` + `@inject IHttpClientFactory HttpClientFactory`
- **Styling**: Tailwind utility classes, slate/indigo color scheme, Heroicons for icons
- **Status badges**: Green for active/approved, gray for inactive/deactivated

### Integration Points
- **Gap: No "list users" endpoint exists** — Need GET endpoint that queries TenantDbContext (tenant-scoped, requires auth with tenant_id claim)
- **Gap: No "update user" endpoint exists** — Need PUT endpoint for name/email/role changes
- **Gap: No "deactivate user" endpoint exists** — Need DELETE endpoint (soft delete via IsDeleted)
- **Gap: No "get single user" endpoint exists** — Need GET /users/{id} for edit page pre-population
- **Gap: No "reset password" endpoint exists** — Need POST endpoint that generates new password, hashes with BCrypt, returns plaintext
- **Gap: CreateUser stores password as plaintext** — Must hash with BCrypt before saving (match LoginEndpoint which uses BCrypt.Verify)
- Existing CreateUser uses AppDbContext (central) not TenantDbContext — may need new tenant-scoped endpoint or fix existing
- AdminSidebar already has a "Users & Roles" section — routing is ready

</code_context>

<specifics>
## Specific Ideas

- Password auto-generation: generate a secure random password (e.g., 12+ chars with mixed case, digits, special), hash with BCrypt for storage, display plaintext once to admin
- The existing CreateUser endpoint uses AppDbContext and takes TenantId explicitly — for admin UI, the tenant-scoped endpoints should use TenantDbContext with TenantId from JWT claims (matching all other tenant-scoped endpoints)
- Role dropdown populated from GET /user-management/roles — but this also uses AppDbContext. Tenant-scoped roles come from TenantDbContext

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 12-user-role-management-ui*
*Context gathered: 2026-04-01*
