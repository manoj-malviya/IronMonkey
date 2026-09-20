# Phase 14: Landing Page - Context

**Gathered:** 2026-04-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Public-facing SaaS landing page at `/` that communicates IronMonkey's value proposition and routes visitors to login or signup. Replaces the default Blazor "Hello, world!" Home.razor. The signup form itself is Phase 15 scope.

</domain>

<decisions>
## Implementation Decisions

### Hero & Copy
- **D-01:** Tagline focuses on lead management: "Manage Every Lead, Your Way" or similar lead-management-focused message
- **D-02:** Subtitle is a one-liner: "Configure your complete lead workflow — fields, pipelines, automation — without writing code."
- **D-03:** Two CTA buttons: "Start Free" (primary, filled indigo-600, links to /signup) and "Sign In" (secondary, outline/text, links to /login)
- **D-04:** Hero includes abstract gradient/mesh background decoration behind the text — no product screenshots
- **D-05:** Minimal top nav bar: IronMonkey logo on left, "Sign In" text link on right

### Feature Showcase
- **D-06:** 4 features in a 2x2 grid: multi-tenancy, configurable leads, industry recipes, pipeline management
- **D-07:** Each feature card has: Heroicon/SVG icon + bold title + 1-2 sentence description

### Visual Tone
- **D-08:** Dark gradient hero section (indigo/slate gradient background, white text) — dramatic modern feel like Linear/Vercel
- **D-09:** Alternating section backgrounds: hero on dark gradient, features on white, footer on dark
- **D-10:** Minimal footer with copyright: "© 2026 IronMonkey. All rights reserved."

### Layout Routing
- **D-11:** Create PublicLayout.razor (no admin sidebar/topbar) for public pages (Home, Login, Signup). Admin pages keep existing MainLayout with AdminSidebar/AdminTopbar.
- **D-12:** Public pages use `@layout PublicLayout` attribute to opt into the clean layout

### Claude's Discretion
- Exact gradient colors and mesh pattern for hero background
- Heroicon selection for each feature card
- Exact copy for feature titles and descriptions
- Typography sizes and spacing
- Transition from dark hero to light features section

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Existing UI patterns
- `IronMonkey.Web/Components/Pages/Login.razor` — Established Tailwind styling: indigo-600 primary, slate palette, rounded-2xl cards, shadow-sm borders
- `IronMonkey.Web/Components/Layout/MainLayout.razor` — Current admin layout with AdminSidebar + AdminTopbar that public pages must NOT use
- `IronMonkey.Web/Components/Pages/Home.razor` — File to replace (currently default Blazor placeholder)

### Auth and routing
- `IronMonkey.Web/Components/App.razor` — Root component, may need layout routing changes
- `IronMonkey.Web/Components/Routes.razor` — Blazor router configuration

### Build pipeline
- `IronMonkey.Web/IronMonkey.Web.csproj` — Tailwind CSS v4 standalone CLI integration via MSBuild BeforeTargets

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- Login.razor Tailwind classes: `bg-gradient-to-br from-slate-100 to-slate-50`, `rounded-2xl`, `shadow-sm`, `border-slate-200`, `bg-indigo-600` — reuse color palette
- AdminSidebar.razor and AdminTopbar.razor — admin-only components, NOT for public pages

### Established Patterns
- Tailwind CSS v4 via standalone CLI (no Node.js) — all styling is utility-first in Razor files
- No component libraries (no MudBlazor, no Syncfusion) — native Razor + Tailwind only
- Blazor Server rendering — all pages are server-rendered

### Integration Points
- Home.razor at `@page "/"` — direct replacement, same route
- Routes.razor — may need `@layout` directive changes or route-level layout assignment
- _Imports.razor — may need new `@using` for PublicLayout namespace

</code_context>

<specifics>
## Specific Ideas

- Modern SaaS feel like Stripe/Linear — clean, professional, minimal
- "Start Free" implies low barrier to entry even though signup requires admin approval
- Dark hero creates visual distinction from the light admin UI — visitors know they're on the marketing site

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 14-landing-page*
*Context gathered: 2026-04-03*
