# Phase 14: Landing Page - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-04-03
**Phase:** 14-landing-page
**Areas discussed:** Hero & Copy, Feature Showcase, Visual Tone, Layout Routing

---

## Hero & Copy

| Option | Description | Selected |
|--------|-------------|----------|
| Lead management focus | e.g., 'Manage Every Lead, Your Way' | ✓ |
| Multi-tenant SaaS focus | e.g., 'One Platform, Every Industry' | |
| Simplicity focus | e.g., 'Lead Management Without Code' | |

**User's choice:** Lead management focus
**Notes:** Tagline should emphasize configurable lead tracking

---

| Option | Description | Selected |
|--------|-------------|----------|
| Start Free + Sign In | 'Start Free' links to /signup, 'Sign In' links to /login | ✓ |
| Get Started + Sign In | 'Get Started' links to /signup, 'Sign In' links to /login | |
| Request Access + Sign In | 'Request Access' links to /signup (admin approval needed) | |

**User's choice:** Start Free + Sign In

---

| Option | Description | Selected |
|--------|-------------|----------|
| One-liner | Short sentence describing value prop | ✓ |
| Two lines | Brief paragraph with target audience | |
| You decide | Claude picks appropriate copy | |

**User's choice:** One-liner subtitle

---

| Option | Description | Selected |
|--------|-------------|----------|
| Abstract gradient | Decorative gradient/mesh background | ✓ |
| Placeholder mockup | Styled dashboard screenshot placeholder | |
| Text only | Clean text hero, relies on typography | |

**User's choice:** Abstract gradient background

---

| Option | Description | Selected |
|--------|-------------|----------|
| Minimal nav | Logo on left, Sign In on right | ✓ |
| Full nav | Logo, Features anchor, Sign In, Get Started | |
| No nav bar | Hero starts at top, CTAs in hero | |

**User's choice:** Minimal nav bar

---

| Option | Description | Selected |
|--------|-------------|----------|
| Primary + secondary | Start Free filled indigo, Sign In outline/text | ✓ |
| Both prominent | Both buttons filled, different colors | |
| You decide | Claude picks visual hierarchy | |

**User's choice:** Primary/secondary button hierarchy

---

| Option | Description | Selected |
|--------|-------------|----------|
| Minimal footer | Copyright line only | ✓ |
| No footer | Page ends after last section | |
| You decide | Claude picks footer | |

**User's choice:** Minimal copyright footer

---

## Feature Showcase

| Option | Description | Selected |
|--------|-------------|----------|
| 4 features | 2x2 grid: multi-tenancy, leads, recipes, pipelines | ✓ |
| 6 features | 2x3 grid: add workflow automation and dashboards | |
| You decide | Claude picks the right number | |

**User's choice:** 4 features in 2x2 grid

---

| Option | Description | Selected |
|--------|-------------|----------|
| Icon + title + description | Heroicon, bold title, 1-2 sentence description | ✓ |
| Title + description only | No icons, text-only cards | |
| Icon + title only | Compact cards, no description | |

**User's choice:** Icon + title + description per feature card

---

## Visual Tone

| Option | Description | Selected |
|--------|-------------|----------|
| Dark gradient hero | Dark indigo/slate gradient, white text (Linear/Vercel style) | ✓ |
| Light with gradient accent | Light bg with subtle gradient mesh (Stripe style) | |
| White + indigo accent | Consistent with login page style | |

**User's choice:** Dark gradient hero

---

| Option | Description | Selected |
|--------|-------------|----------|
| Alternating sections | Hero dark, features white, footer dark | ✓ |
| Same background | Consistent bg, sections by spacing | |
| You decide | Claude picks visual flow | |

**User's choice:** Alternating section backgrounds

---

## Layout Routing

| Option | Description | Selected |
|--------|-------------|----------|
| Two layouts | PublicLayout.razor + MainLayout.razor, @layout attribute per page | ✓ |
| Conditional layout | MainLayout checks route, shows/hides sidebar | |
| You decide | Claude picks cleanest Blazor pattern | |

**User's choice:** Separate PublicLayout.razor for public pages

---

## Claude's Discretion

- Exact gradient colors and mesh pattern
- Heroicon selection for feature cards
- Feature titles and description copy
- Typography sizes and spacing
- Dark-to-light section transition

## Deferred Ideas

None — discussion stayed within phase scope
