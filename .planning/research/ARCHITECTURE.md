# Architecture Patterns: Blazor Server Admin UI Integration

**Domain:** Multi-tenant SaaS CRM with admin UI
**Researched:** 2026-03-27
**Confidence:** HIGH (based on existing codebase inspection + Blazor Server patterns)

## Executive Summary

The Blazor Server admin UI integrates with the existing Minimal API backend via HTTP client calls with JWT authentication. The architecture leverages:

1. **JWT token storage**: HTTP-only cookies (provided by `AddServerSideBlazor` with MapDefaultEndpoints) or explicit token management in Blazor AuthenticationStateProvider
2. **HTTP client interceptor**: Injects JWT from auth state into every API request via HttpClient pipeline
3. **Scoped auth state**: CascadingAuthenticationState component provides current user/tenant context to Blazor components
4. **Multi-tenant isolation**: tenant_id claim extracted from JWT ensures database-per-tenant access without extra parameters
5. **Layout hierarchy**: Root App.razor wraps layouts with CascadingAuthenticationState, routes are protected via AuthorizeRouteView

The design maintains the existing API contract—no backend changes required. The web project adds UI layers only.

## Recommended Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     IronMonkey.Web (Blazor Server)               │
├─────────────────────────────────────────────────────────────────┤
│  App.razor (root with CascadingAuthenticationState)              │
│      └─ MainLayout.razor (sidebar nav, dashboard shell)         │
│           └─ Routes (AuthorizeRouteView for role protection)    │
│                ├─ LoginPage.razor (public, no auth required)    │
│                ├─ AdminPages/                                    │
│                │  ├─ RecipeManagement/                          │
│                │  │  ├─ RecipeList.razor                        │
│                │  │  ├─ RecipeCreate.razor                      │
│                │  │  ├─ RecipeEdit.razor                        │
│                │  │  └─ RecipePreview.razor                     │
│                │  ├─ TenantManagement/                          │
│                │  │  ├─ SignupRequestList.razor                 │
│                │  │  ├─ TenantApprovalForm.razor                │
│                │  │  └─ TenantStatusOverview.razor              │
│                │  ├─ UserManagement/                            │
│                │  │  ├─ UserList.razor                          │
│                │  │  ├─ UserCreate.razor                        │
│                │  │  └─ RoleAssignment.razor                    │
│                │  └─ SystemConfiguration/                       │
│                │     ├─ PipelineStageConfig.razor               │
│                │     ├─ CustomFieldsConfig.razor                │
│                │     ├─ LeadRoutingConfig.razor                 │
│                │     └─ WorkflowRuleConfig.razor                │
│                └─ Shared/                                        │
│                   ├─ ErrorBoundary.razor                        │
│                   ├─ LoadingSpinner.razor                       │
│                   └─ FormComponents/                            │
│                                                                  │
│  Services/                                                       │
│  ├─ AuthenticationService.cs (login, logout, token mgmt)        │
│  ├─ RecipeApiClient.cs (recipe CRUD endpoints)                  │
│  ├─ TenantApiClient.cs (tenant/signup endpoints)                │
│  ├─ UserApiClient.cs (user/role endpoints)                      │
│  ├─ ConfigurationApiClient.cs (pipeline/field/workflow configs) │
│  └─ HttpClientInterceptor.cs (JWT injection)                    │
│                                                                  │
│  Auth/                                                           │
│  ├─ CustomAuthenticationStateProvider.cs (JWT extraction)       │
│  └─ AuthorizedHttpClient.cs (auto-injects Authorization header) │
│                                                                  │
│  Models/                                                         │
│  ├─ RecipeModels.cs (DTO models matching API)                   │
│  ├─ TenantModels.cs                                             │
│  ├─ UserModels.cs                                               │
│  └─ ConfigModels.cs                                             │
└─────────────────────────────────────────────────────────────────┘
                            ↓ HTTP + JWT
┌─────────────────────────────────────────────────────────────────┐
│              IronMonkey.ApiService (Minimal API)                 │
├─────────────────────────────────────────────────────────────────┤
│  Authentication/Endpoints/                                       │
│  ├─ LoginEndpoint (POST /auth/login) → JWT token                │
│  └─ (logout handled client-side in Blazor)                      │
│                                                                  │
│  Features/Recipes/                                               │
│  ├─ RecipeListEndpoint (GET /api/recipes)                       │
│  ├─ CreateRecipeEndpoint (POST /api/recipes)                    │
│  ├─ UpdateRecipeEndpoint (PUT /api/recipes/{id})                │
│  ├─ DeactivateRecipeEndpoint (POST /api/recipes/{id}/deactivate)│
│  └─ RecipePreviewEndpoint (GET /api/recipes/{id}/preview)       │
│                                                                  │
│  Features/Leads/                                                 │
│  ├─ PipelineStages/* (custom field, pipeline config)            │
│  ├─ CustomFields/* (field definition management)                │
│  └─ Workflow/Rules/* (workflow rule CRUD)                       │
│                                                                  │
│  (All existing endpoints, no modifications required)            │
└─────────────────────────────────────────────────────────────────┘
```

## Component Boundaries

| Component | Responsibility | Communicates With | Notes |
|-----------|---------------|-------------------|-------|
| **LoginPage.razor** | Login form, credential submission, token storage | AuthenticationService | Public page, no AuthorizeView |
| **MainLayout.razor** | Sidebar nav, breadcrumbs, user menu, logout button | AuthenticationService, NavMenuComponent | Cascades AuthenticationState to child routes |
| **AuthorizeRouteView** | Route-level auth guard, role-based access | CustomAuthenticationStateProvider | Denies access if not authenticated or wrong role |
| **RecipeList.razor** | Display paginated recipe list, delete/edit buttons | RecipeApiClient | Calls GET /api/recipes (public but only shows to admins) |
| **RecipeCreate.razor** | Form for new recipe, validation, submit | RecipeApiClient, CustomFieldEditor | Calls POST /api/recipes with content model |
| **TenantApprovalForm.razor** | Approve/reject signup requests, notes | TenantApiClient | Calls POST /signup/{id}/approve or /reject |
| **UserList.razor** | Tenant user list with role assignments | UserApiClient | Scoped to current tenant via JWT tenant_id claim |
| **CustomAuthenticationStateProvider** | Extract/provide JWT claims to Blazor auth system | Jwt parser (no API call) | Loads claims from JWT on app load and after login |
| **AuthorizedHttpClient** | Intercept requests, add JWT Authorization header | IHttpClientFactory | Factory pattern ensures fresh token per request |
| **RecipeApiClient** | Recipe CRUD endpoint calls | AuthorizedHttpClient | Type-safe DTOs matching API contracts |

## Data Flow

### 1. Login Flow
```
User enters email/password
         ↓
LoginPage.razor calls AuthenticationService.Login(email, password)
         ↓
AuthenticationService POSTs to /auth/login with credentials
         ↓
API returns JWT token + TenantId response
         ↓
AuthenticationService stores JWT in secure storage (http-only cookie or IndexedDB)
         ↓
CustomAuthenticationStateProvider loads JWT, extracts claims
         ↓
NotifyAuthenticationStateChanged fires, UI updates (navbar shows user name/tenant)
         ↓
Router redirects to dashboard (AuthorizeRouteView allows entry)
```

### 2. API Request with Tenant Isolation
```
Component renders, calls RecipeApiClient.ListAsync()
         ↓
RecipeApiClient creates HttpRequestMessage for GET /api/recipes
         ↓
AuthorizedHttpClient.Send() intercepts:
  - Retrieves JWT from CustomAuthenticationStateProvider
  - Adds Authorization: Bearer {jwt} header
  - Extracts tenant_id from JWT claim (no extra query param)
         ↓
HttpClient sends request with Authorization header
         ↓
API ValidateToken in JwtBearer middleware:
  - Validates signature
  - Extracts claims including tenant_id
  - Populates User.FindFirst("tenant_id")
         ↓
IUserContext injected in endpoint handler:
  - Reads tenant_id from HttpContext.User
  - TenantDbContextFactory creates db context for tenant
         ↓
Query executes against tenant's database
         ↓
Response returned to Blazor component
```

### 3. Form Submission with Validation
```
User fills recipe form, clicks Save
         ↓
Component validates locally (FluentValidation Blazor or similar)
         ↓
Component calls RecipeApiClient.CreateAsync(request)
         ↓
API endpoint validates request with FluentValidation
         ↓
If validation errors: API returns BadRequest with error details
         ↓
Component receives ValidationError, displays field-level errors
         ↓
If valid: Record created, API returns 201 Created
         ↓
Component shows success toast, updates list
```

## Integration Points with Existing Backend

### 1. Authentication
- **No backend changes required**. LoginEndpoint already exists, returns JWT with tenant_id claim.
- Blazor calls POST /auth/login, stores token client-side, extracts claims for subsequent requests.

### 2. Recipe Management
- **No backend changes required**. All endpoints exist (RecipeListEndpoint, CreateRecipeEndpoint, UpdateRecipeEndpoint, DeactivateRecipeEndpoint).
- Blazor adds UI pages for list, create, edit, preview. Components call existing endpoints.

### 3. Tenant Management
- **No backend changes required**. Signup endpoints exist (ListSignupRequestsEndpoint, ApproveTenantEndpoint, RejectTenantEndpoint).
- Blazor adds admin pages to approve/reject signups.

### 4. User & Role Management
- **No backend changes required**. CreateUserEndpoint, ListRoles endpoints exist.
- Blazor adds UI pages to list users, assign roles.

### 5. Pipeline & Field Configuration
- **May need new read endpoints**. Existing endpoints support CRUD; UI needs GET endpoints for configuration forms.
- Example: PipelineStageListEndpoint (GET /api/pipeline-stages) if not already present.
- Check Features/Leads/PipelineStages/* and Features/Leads/CustomFields/* directories for existing endpoints.

### 6. Workflow Rule Management
- **May need new read/list endpoints**. WorkflowRuleEvaluationJob exists; need UI read endpoint for forms.
- Example: ListWorkflowRulesEndpoint (GET /api/tenant/{tenantId}/workflow-rules).

### New Blazor Components (No Backend Impact)
- LoginPage.razor — purely UI, calls existing /auth/login
- MainLayout.razor — routing/navigation only
- Error boundaries, loading spinners, form components — all pure UI

### Modified Blazor/Aspire Components
- **AppHost.cs**: Uncomment Web project reference, add to ServiceDefaults
- **Program.cs** (Web): Register services, add authentication middleware, setup CascadingAuthenticationState

## Build Order Recommendations

1. **Phase 1: Foundation** (Prerequisites)
   - Update AppHost.cs to include Web project in service discovery
   - Add service registration in Web/Program.cs (auth, HttpClient, DI services)
   - Create CustomAuthenticationStateProvider
   - Uncomment CascadingAuthenticationState in App.razor

2. **Phase 2: Auth** (Enables all admin features)
   - Create LoginPage.razor with form validation
   - Create AuthenticationService for login logic
   - Create MainLayout.razor with logout button
   - Test: Login → see JWT claim extraction working

3. **Phase 3: Recipe Management** (Demonstrates full CRUD pattern)
   - Create RecipeApiClient service
   - Create RecipeList.razor, RecipeCreate.razor, RecipeEdit.razor
   - Create shared RecipePreview component (used in list and create)
   - Test: List, create, edit, delete recipes

4. **Phase 4: Tenant Management** (Admin functions)
   - Create TenantApiClient service
   - Create SignupRequestList.razor, ApprovalForm.razor
   - Create TenantStatusOverview.razor
   - Test: Approve/reject signups, see tenant status

5. **Phase 5: User & Role Management** (Tenant admin)
   - Create UserApiClient service
   - Create UserList.razor, UserCreate.razor, RoleAssignment.razor
   - Test: Create users, assign roles within tenant

6. **Phase 6: System Configuration** (Advanced)
   - Create ConfigurationApiClient service
   - Create PipelineStageConfig.razor, CustomFieldsConfig.razor, etc.
   - May require new read endpoints in API (check existing first)

7. **Phase 7: Polish** (UX/styling)
   - Add Tailwind CSS (standalone CLI)
   - Add error boundaries, loading spinners
   - Add form validation feedback
   - Add toast notifications for success/error

## Dependencies & Blockers

### No Blockers
- All existing API endpoints support the required operations
- Blazor Server model is compatible with Aspire service discovery
- JWT authentication already in place (no new auth logic needed)

### Minor Considerations
- **Configuration read endpoints**: Check if all required GET endpoints exist for configuration forms. Example: Pipeline stages list, custom fields list, workflow rules list. If missing, add simple read endpoints.
- **Tailwind CSS setup**: Standalone CLI has no build dependencies—straightforward integration.
- **Error handling UX**: API may return different error formats; standardize error response handling in ApiClient services.

## Patterns to Follow

### Pattern 1: Service-Based API Abstraction
**What:** Encapsulate all API calls in typed client services with DTOs.

**When:** Always. Keeps components clean, enables unit testing, centralizes request building.

### Pattern 2: Cascading Auth State with CustomAuthenticationStateProvider
**What:** Derive AuthenticationStateProvider from inherited claims-based provider; expose current user via public property for component access.

**When:** Every Blazor Server app needs auth state propagation. Provides both AuthorizeView guards and component-level access.

### Pattern 3: Authorized HttpClient with JWT Injection
**What:** Create HttpClient factory that adds Authorization header before every request.

**When:** All authenticated requests. Centralizes token management, no per-component JWT handling.

### Pattern 4: Role-Based Route Protection with AuthorizeRouteView
**What:** Wrap Router with AuthorizeRouteView that checks auth state before rendering.

**When:** Protecting admin pages. Denies access unless user is authenticated AND has required role.

### Pattern 5: Form Validation with EditForm and ValidationMessage
**What:** Use EditForm + DataAnnotationsValidator + ValidationMessage for field-level errors.

**When:** Any form submission. Matches API validation errors with form fields.

## Anti-Patterns to Avoid

### Anti-Pattern 1: Storing JWT in Blazor Component State
**What:** Using @code to store raw JWT tokens as component fields.

**Why bad:** Tokens visible in component memory, no protection from XSS, no refresh mechanism.

### Anti-Pattern 2: Calling API Endpoints Directly in OnInitializedAsync Without Error Handling
**What:** Missing try-catch and error states in async initialization.

**Why bad:** No error handling, no loading state, components crash if API is slow or fails.

### Anti-Pattern 3: Passing HttpClient.DefaultRequestHeaders to Every Component
**What:** Manually adding Authorization headers in multiple components instead of centralizing.

**Why bad:** Code duplication, inconsistent token handling, easy to forget a request.

### Anti-Pattern 4: No Role-Based Authorization on Routes
**What:** Any authenticated user can access admin pages.

**Why bad:** Tenant users can accidentally see admin pages meant only for platform superadmins.

### Anti-Pattern 5: Mixing API Calls with Component Logic
**What:** Complex HTTP calls with retry logic directly in component code.

**Why bad:** Hard to test, unmaintainable, leaky abstraction.

## New vs Modified Components

### New Components
- `IronMonkey.Web/Components/Pages/Admin/RecipeList.razor`
- `IronMonkey.Web/Components/Pages/Admin/RecipeCreate.razor`
- `IronMonkey.Web/Components/Pages/Admin/RecipeEdit.razor`
- `IronMonkey.Web/Components/Pages/Admin/TenantManagement/`
- `IronMonkey.Web/Components/Pages/Admin/UserManagement/`
- `IronMonkey.Web/Components/Pages/Admin/SystemConfig/`
- `IronMonkey.Web/Components/Pages/LoginPage.razor`
- `IronMonkey.Web/Auth/CustomAuthenticationStateProvider.cs`
- `IronMonkey.Web/Services/RecipeApiClient.cs`
- `IronMonkey.Web/Services/TenantApiClient.cs`
- `IronMonkey.Web/Services/UserApiClient.cs`
- `IronMonkey.Web/Services/ConfigurationApiClient.cs`

### Modified Components
- `IronMonkey.Web/Program.cs`
- `IronMonkey.Web/Components/App.razor`
- `IronMonkey.Web/Components/Layout/MainLayout.razor`
- `IronMonkey.Web/Components/Layout/NavMenu.razor`
- `IronMonkey.AppHost/AppHost.cs`

### Unchanged Components
- All API endpoints (IronMonkey.ApiService)
- Database entities (IronMonkey.Data)
- Authentication/authorization middleware

## Sources

Based on direct codebase inspection of IronMonkey repository.
