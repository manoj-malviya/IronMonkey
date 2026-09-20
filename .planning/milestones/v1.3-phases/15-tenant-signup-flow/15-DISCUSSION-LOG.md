# Phase 15: Tenant Signup Flow - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-04-03
**Phase:** 15-tenant-signup-flow
**Areas discussed:** Form Layout, Recipe Browser, Confirmation UX, Cross-links

---

## Form Layout

| Option | Description | Selected |
|--------|-------------|----------|
| Single card form | All fields in one card, matching Login.razor style | ✓ |
| Grouped sections | Two groups: Company Info + Admin Account + Recipe | |
| Multi-step wizard | Step 1: Company, Step 2: Account, Step 3: Recipe | |

**User's choice:** Single card form

---

| Option | Description | Selected |
|--------|-------------|----------|
| Same as login (max-w-md) | Consistent narrow card | ✓ |
| Wider card (max-w-lg) | More breathing room | |
| Two-column (max-w-2xl) | Side-by-side fields on desktop | |

**User's choice:** Same as login (max-w-md)

---

## Recipe Browser

| Option | Description | Selected |
|--------|-------------|----------|
| Clickable cards | Cards with name + type, click to expand preview, selected gets border | ✓ |
| Dropdown + preview | Select dropdown, preview panel below | |
| Radio list + preview | Radio buttons with expandable preview | |

**User's choice:** Clickable cards with expandable preview

---

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, first option | 'Start blank' as first/default | ✓ |
| Yes, last option | 'Start blank' at end | |
| No, just optional | Skip selection entirely | |

**User's choice:** Blank option first

---

## Confirmation UX

| Option | Description | Selected |
|--------|-------------|----------|
| In-place success | Form replaced by success card | |
| Separate page | Navigate to /signup/success | ✓ |
| You decide | Claude picks best UX | |

**User's choice:** Separate /signup/success page

---

## Cross-links

| Option | Description | Selected |
|--------|-------------|----------|
| Below form card | Small text links below each form card | ✓ |
| Inside form card | Links at bottom of form, before submit | |
| You decide | Claude picks placement | |

**User's choice:** Below form card

---

## Claude's Discretion

- Validation error messages per field
- Password strength indicator
- Recipe preview layout
- Success page icon/illustration
- Form spacing and padding

## Deferred Ideas

None — discussion stayed within phase scope
