# Phase 9: UI Foundation & Auth - Context

**Gathered:** 2026-03-31
**Status:** Ready for planning

<domain>
## Phase Boundary

Build the Blazor Server admin shell: Tailwind CSS setup via standalone CLI, layout with sidebar navigation, login page, JWT authentication with ProtectedSessionStorage, auth guards on all admin routes, and logout. This phase establishes the UI foundation that all subsequent phases (10-13) build on.

</domain>

<decisions>
## Implementation Decisions

### Login Page Design
- **D-01:** Centered card form on a neutral (gray/slate) background — clean, professional admin pattern
- **D-02:** Validation errors display inline below each field with a summary banner at top of form
- **D-03:** Branding is app name ("IronMonkey") with minimal logo area — can enhance later
- **D-04:** Loading state during authentication: disabled submit button with spinner text ("Signing in...")

### Sidebar Navigation Structure
- **D-05:** Nav items grouped by domain with section headers — groups: Recipes, Tenants, Users & Roles, Configuration
- **D-06:** Icons use Heroicons (outline style) — pairs well with Tailwind, MIT licensed
- **D-07:** Active nav state: background highlight + left border accent color
- **D-08:** User info (name + role) displayed at bottom of sidebar

### Auth Session Management
- **D-09:** JWT stored in ProtectedSessionStorage (carried forward from v1.2 key decisions)
- **D-10:** Token expiry handling: redirect to login page with "Session expired" message — no silent refresh for v1.2
- **D-11:** Auth guard via custom AuthenticationStateProvider + CascadingAuthenticationState (Blazor-native pattern)
- **D-12:** Login form starts clean each time — no "remember email" feature

### Responsive Layout
- **D-13:** Hamburger button toggles overlay sidebar on mobile screens
- **D-14:** Mobile breakpoint at 768px (Tailwind `md` breakpoint)
- **D-15:** Sidebar is fixed on desktop, overlays on mobile — content area goes full-width on mobile
- **D-16:** Slim topbar on mobile with hamburger icon + app name for navigation access

### Claude's Discretion
- Tailwind color palette selection (recommend slate/indigo or similar professional scheme)
- Exact spacing and sizing values within Tailwind utility classes
- Animation/transition details for sidebar collapse
- Error page styling (401, 404, 500)
- Favicon and any minimal branding assets

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Backend Auth API
- `IronMonkey.ApiService/Authentication/Endpoints/LoginEndpoint.cs` — POST `/auth/login` endpoint, returns `{ Token, TenantId }`, uses BCrypt + JWT
- `IronMonkey.Common/Auth/` — JWT generation, `LoggedInUser` record, `JwtOptions` configuration

### Existing Web Project
- `IronMonkey.Web/Program.cs` — Blazor Server setup with Aspire service defaults, HttpClient configured for `apiservice`
- `IronMonkey.Web/Components/App.razor` — Root component, currently references Bootstrap (must replace with Tailwind)
- `IronMonkey.Web/Components/Layout/MainLayout.razor` — Default template layout (must replace)
- `IronMonkey.Web/Components/Layout/NavMenu.razor` — Default template nav (must replace)
- `IronMonkey.Web/ApiClient.cs` — Existing API client with Aspire service discovery

### Research & Setup Guides
- `.planning/research/ADMIN_UI_QUICK_SETUP.md` — Tailwind standalone CLI setup, JwtAuthenticationStateProvider pattern, DelegatingHandler pattern
- `.planning/research/IMPLEMENTATION_PATTERNS_BLAZOR.md` — Blazor UI implementation patterns
- `.planning/research/PITFALLS_BLAZOR_UI.md` — Known Blazor UI pitfalls to avoid

### Project Configuration
- `.planning/REQUIREMENTS.md` — UIFN-01 through UIFN-06 (this phase's requirements)
- `.planning/PROJECT.md` — Key decisions: Tailwind standalone CLI, no MudBlazor, JWT in ProtectedSessionStorage

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `ApiClient.cs`: Typed HttpClient with Aspire service discovery to `apiservice` — foundation for API calls from Blazor
- `IronMonkey.ServiceDefaults`: Shared Aspire service defaults (telemetry, health checks) already referenced by Web project
- `LoginEndpoint.cs`: Backend auth endpoint ready to consume — POST `/auth/login` with email/password

### Established Patterns
- **Endpoint pattern**: Minimal API with `IEndpoint` interface, nested Request/Response records, FluentValidation
- **JWT claims**: Token includes `tenant_id`, user name, email, role — consume these in AuthenticationStateProvider
- **Aspire service discovery**: HttpClient uses `https+http://apiservice` base address — must use same pattern for auth calls

### Integration Points
- `IronMonkey.AppHost/Program.cs`: Web project currently commented out — must uncomment and wire up
- `App.razor`: Must swap Bootstrap CSS references for Tailwind CSS output
- `Program.cs` (Web): Must add AuthenticationStateProvider, ProtectedSessionStorage, auth services
- MSBuild integration needed for Tailwind CLI to run during build

</code_context>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches. Key constraint: no Node.js toolchain (Tailwind standalone CLI only), no component libraries (native Razor + Tailwind).

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 09-ui-foundation-auth*
*Context gathered: 2026-03-31*
