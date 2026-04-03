# Roadmap: v1.3 Public Landing & Tenant Signup

**Milestone:** v1.3 Public Landing & Tenant Signup
**Created:** 2026-04-03
**Granularity:** Standard
**Phase range:** 14–15 (continuing from v1.2 which ended at Phase 13)
**Coverage:** 10/10 requirements mapped

## Phases

- [x] **Phase 14: Landing Page** — Visitor sees a compelling, responsive public landing page with navigation to login and signup (completed 2026-04-03)
- [ ] **Phase 15: Tenant Signup Flow** — Visitor can complete the full signup process including recipe selection and confirmation

## Phase Details

### Phase 14: Landing Page
**Goal**: Visitors encounter a modern, responsive SaaS landing page that communicates IronMonkey's value and routes them to the right action
**Depends on**: Nothing (first phase of milestone)
**Requirements**: LAND-01, LAND-02, LAND-03, LAND-04, NAV-01
**Success Criteria** (what must be TRUE):
  1. Visitor landing at / sees a hero section with tagline, product description, and CTA buttons (Get Started, Login)
  2. Visitor can scroll through a feature grid showcasing multi-tenancy, configurable leads, industry recipes, and pipeline management
  3. Page layout reflows correctly at desktop (1280px+), tablet (768px), and mobile (375px) breakpoints
  4. Default Blazor content is fully replaced — Home.razor renders the new landing page with no placeholder text
  5. Navbar contains clearly visible links to /login and /signup
**Plans**: 3 plans

Plans:
- [x] 14-01-PLAN.md — PublicLayout.razor + FeatureCard.razor (layout shell and reusable card component)
- [x] 14-02-PLAN.md — Home.razor replacement (hero section, feature grid, responsive landing page)
- [x] 14-03-PLAN.md — Routes.razor + Login.razor wiring (public route access, login page uses PublicLayout)

**UI hint**: yes

### Phase 15: Tenant Signup Flow
**Goal**: A prospective tenant can complete a signup form, optionally select an industry recipe, and receive confirmation that their request was submitted
**Depends on**: Phase 14
**Requirements**: SIGN-01, SIGN-02, SIGN-03, SIGN-04, NAV-02
**Success Criteria** (what must be TRUE):
  1. Visitor at /signup can fill in company name, admin email, password, phone, company size, address, and billing contact and submit the form
  2. Visitor can browse the recipe catalog during signup, expand a recipe preview, and optionally select one before submitting
  3. After successful submission, visitor is shown a confirmation page indicating their request is pending admin review
  4. Submitting with any required field empty shows an inline validation error on that field without navigating away
  5. Login page shows a link to /signup; signup page shows a link to /login
**Plans**: 3 plans

Plans:
- [x] 15-01-PLAN.md — RecipePreviewCard.razor + RecipePreviewPanel.razor (reusable recipe selection components)
- [ ] 15-02-PLAN.md — Signup.razor (full signup form wired to POST /auth/signup with recipe browser)
- [ ] 15-03-PLAN.md — SignupSuccessPage.razor + Login.razor cross-link + Routes.razor /signup/success

**UI hint**: yes

## Progress Table

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 14. Landing Page | 3/3 | Complete    | 2026-04-03 |
| 15. Tenant Signup Flow | 1/3 | In Progress|  |

---
*Roadmap created: 2026-04-03 for milestone v1.3*
