# Phase 15: Tenant Signup Flow - Context

**Gathered:** 2026-04-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Tenant self-service signup form at `/signup` wired to existing POST /auth/signup API. Includes optional industry recipe selection with preview, form validation, confirmation page, and cross-links between login and signup. No backend changes needed — all API endpoints exist.

</domain>

<decisions>
## Implementation Decisions

### Form Layout
- **D-01:** Single card form matching Login.razor's style — all fields in one centered card (max-w-md), scrollable
- **D-02:** Field order: CompanyName, AdminEmail, AdminPassword, Phone, CompanySize, Address, BillingContact, then Recipe selection below
- **D-03:** Uses EditForm + DataAnnotationsValidator + inline error messages (same pattern as Login.razor)
- **D-04:** Loading state on submit button ("Submitting..." disabled state) matching Login.razor pattern

### Recipe Browser
- **D-05:** Clickable cards showing recipe name + industry type. Click to expand preview below showing stages/fields/rules from GET /api/recipes/{id}
- **D-06:** Selected card gets indigo border highlight
- **D-07:** "Start blank — configure everything yourself" as the first/default option (sends null RecipeId)
- **D-08:** Recipe list fetched from GET /api/recipes on page load (AllowAnonymous)

### Confirmation UX
- **D-09:** Navigate to separate /signup/success page after successful submission
- **D-10:** Success page shows check icon, "Your request has been submitted!", message about admin review, and links to home + login
- **D-11:** Success page uses PublicLayout.razor

### Cross-links
- **D-12:** "Already have an account? Sign in" text link below the signup form card, linking to /login
- **D-13:** "Don't have an account? Sign up" text link below the login form card, linking to /signup
- **D-14:** Small text link style (text-sm text-slate-600)

### Layout
- **D-15:** Signup page uses `@layout PublicLayout` (same as Login.razor and Home.razor from Phase 14)
- **D-16:** Routes.razor already allows /signup without auth (added in Phase 14 plan 14-03)

### Claude's Discretion
- Exact validation error messages for each field
- Password strength indicator (if any)
- Recipe preview layout and content formatting
- Spacing and padding within the form card
- Success page illustration/icon choice

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Signup API
- `IronMonkey.ApiService/Authentication/Endpoints/SignupRequestEndpoint.cs` — POST /auth/signup request shape: CompanyName, AdminEmail, AdminPassword, Phone, RecipeId (Guid?), CompanySize, Address, BillingContact. Response: 201 Created with SignupRequestId + Message
- `IronMonkey.ApiService/Authentication/Endpoints/SignupRequestEndpoint.cs` — RequestValidator: all fields required except RecipeId, email must be valid, password min 8 chars

### Recipe API
- `IronMonkey.ApiService/Features/Recipes/RecipeListEndpoint.cs` — GET /api/recipes (AllowAnonymous) — returns list of active recipes
- `IronMonkey.ApiService/Features/Recipes/RecipePreviewEndpoint.cs` — GET /api/recipes/{id} (AllowAnonymous) — returns recipe with content (stages, fields, rules)

### Existing UI patterns
- `IronMonkey.Web/Components/Pages/Login.razor` — Reference implementation for form card styling, EditForm pattern, error handling, loading state, API call pattern with HttpClientFactory
- `IronMonkey.Web/Components/Layout/PublicLayout.razor` — Public layout used by signup page
- `IronMonkey.Web/Components/Routes.razor` — Already allows /signup path without auth redirect

### Build pipeline
- `IronMonkey.Web/IronMonkey.Web.csproj` — Tailwind CSS v4 standalone CLI via MSBuild

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- Login.razor: EditForm + DataAnnotationsValidator pattern, HttpClientFactory.CreateClient("AdminApi"), error banner, loading state — direct template for signup form
- PublicLayout.razor: Public layout shell with nav and footer — already created in Phase 14
- FeatureCard.razor: Reusable card component (could inspire recipe card styling)

### Established Patterns
- Tailwind CSS v4 utility-first: indigo-600 primary, slate palette, rounded-2xl cards, shadow-sm
- API calls via HttpClientFactory.CreateClient("AdminApi") + PostAsJsonAsync/GetFromJsonAsync
- DataAnnotationsValidator for client-side validation with ValidationMessage components

### Integration Points
- Routes.razor: /signup already in public path exclusion list (from Phase 14-03)
- PublicLayout.razor: @layout directive for signup page
- Login.razor: needs "Don't have an account? Sign up" link added below card

</code_context>

<specifics>
## Specific Ideas

- Signup form should feel like a natural companion to the login page — same card style, same centering, same Tailwind patterns
- Recipe cards should be visually lighter than the main form — they're an optional enhancement, not the core action
- Confirmation page should feel reassuring — the user just committed to trying the product

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 15-tenant-signup-flow*
*Context gathered: 2026-04-03*
