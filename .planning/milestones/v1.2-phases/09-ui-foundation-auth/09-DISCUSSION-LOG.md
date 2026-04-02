# Phase 9: UI Foundation & Auth - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-03-31
**Phase:** 09-ui-foundation-auth
**Areas discussed:** Login page design, Sidebar navigation structure, Auth session management, Responsive layout behavior
**Mode:** Auto (--auto flag — all recommended defaults selected)

---

## Login Page Design

| Option | Description | Selected |
|--------|-------------|----------|
| Centered card on neutral background | Clean, professional admin pattern | ✓ |
| Full-page split (form + hero image) | More visual, heavier to build | |
| Minimal inline form | Too sparse for admin tool | |

**User's choice:** [auto] Centered card on neutral background (recommended default)

| Option | Description | Selected |
|--------|-------------|----------|
| Inline errors below fields + summary banner | Immediate feedback + overall status | ✓ |
| Toast notifications only | Easy to miss, not ideal for forms | |
| Summary at top only | Requires scrolling to find field issues | |

**User's choice:** [auto] Inline below fields with summary at top (recommended default)

| Option | Description | Selected |
|--------|-------------|----------|
| App name + minimal logo area | Simple, enhanceable later | ✓ |
| Full branded splash | Overbuilt for v1.2 | |
| No branding | Too plain | |

**User's choice:** [auto] App name + minimal logo area (recommended default)

| Option | Description | Selected |
|--------|-------------|----------|
| Disabled button with spinner text | Prevents double-submit, clear feedback | ✓ |
| Overlay spinner on form | More complex, less standard | |
| No loading state | Poor UX | |

**User's choice:** [auto] Disabled button with spinner text (recommended default)

---

## Sidebar Navigation Structure

| Option | Description | Selected |
|--------|-------------|----------|
| Grouped by domain with section headers | Matches admin sections naturally | ✓ |
| Flat list with icons | Simpler but harder to scan at scale | |
| Accordion groups | Interactive but adds complexity | |

**User's choice:** [auto] Grouped by domain with section headers (recommended default)

| Option | Description | Selected |
|--------|-------------|----------|
| Heroicons (outline) | Pairs with Tailwind, MIT license | ✓ |
| Bootstrap Icons | Already in template but moving away from Bootstrap | |
| Lucide Icons | Good alternative, slightly less Tailwind-native | |

**User's choice:** [auto] Heroicons outline style (recommended default)

| Option | Description | Selected |
|--------|-------------|----------|
| Background highlight + left border accent | Clear visual indicator | ✓ |
| Bold text only | Too subtle | |
| Icon color change only | Not accessible enough | |

**User's choice:** [auto] Background highlight + left border accent (recommended default)

| Option | Description | Selected |
|--------|-------------|----------|
| User name + role at bottom | Contextual, doesn't clutter | ✓ |
| User info in topbar | Takes horizontal space | |
| No user info in nav | Missing context | |

**User's choice:** [auto] User name + role at bottom of sidebar (recommended default)

---

## Auth Session Management

| Option | Description | Selected |
|--------|-------------|----------|
| ProtectedSessionStorage | Already decided in v1.2 key decisions | ✓ |
| localStorage | Not secure enough for admin tool | |
| Cookie-based | Different auth pattern than backend expects | |

**User's choice:** [auto] ProtectedSessionStorage (carried forward decision)

| Option | Description | Selected |
|--------|-------------|----------|
| Redirect to login with expired message | Simple, secure, no refresh complexity | ✓ |
| Silent token refresh | Adds backend endpoint + complexity | |
| Warning before expiry | Nice but overbuilt for v1.2 | |

**User's choice:** [auto] Redirect to login with session expired message (recommended default)

| Option | Description | Selected |
|--------|-------------|----------|
| Custom AuthenticationStateProvider + Cascading | Blazor-native pattern | ✓ |
| Manual auth checks per page | Repetitive, error-prone | |
| Third-party auth library | Unnecessary dependency | |

**User's choice:** [auto] AuthenticationStateProvider + CascadingAuthenticationState (recommended default)

| Option | Description | Selected |
|--------|-------------|----------|
| Clean form each time | Simpler, more secure for admin | ✓ |
| Remember last email | Convenience but security tradeoff | |

**User's choice:** [auto] No remember email (recommended default)

---

## Responsive Layout Behavior

| Option | Description | Selected |
|--------|-------------|----------|
| Hamburger toggles overlay sidebar | Standard responsive pattern | ✓ |
| Always-visible mini sidebar | Takes space, complex | |
| Bottom nav on mobile | Not standard for admin tools | |

**User's choice:** [auto] Hamburger toggles overlay sidebar (recommended default)

| Option | Description | Selected |
|--------|-------------|----------|
| 768px (Tailwind md) | Standard tablet/desktop split | ✓ |
| 1024px (Tailwind lg) | Too aggressive — hides sidebar on tablets | |
| 640px (Tailwind sm) | Too small — sidebar on phones is unusual | |

**User's choice:** [auto] 768px md breakpoint (recommended default)

| Option | Description | Selected |
|--------|-------------|----------|
| Fixed sidebar desktop, overlay mobile | Maximizes content space | ✓ |
| Always overlay | Wastes desktop space | |
| Always fixed | Breaks on mobile | |

**User's choice:** [auto] Fixed desktop, overlay mobile (recommended default)

| Option | Description | Selected |
|--------|-------------|----------|
| Slim topbar with hamburger + app name | Provides nav access point | ✓ |
| No topbar on mobile | No way to open nav | |
| Full topbar with actions | Too complex for v1.2 | |

**User's choice:** [auto] Slim topbar with hamburger + app name (recommended default)

---

## Claude's Discretion

- Tailwind color palette, spacing, sizing values
- Animation/transition details for sidebar
- Error page styling
- Favicon and branding assets

## Deferred Ideas

None — discussion stayed within phase scope.
