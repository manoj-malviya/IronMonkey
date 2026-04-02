# Phase 10: Recipe Management UI - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-04-01
**Phase:** 10-recipe-management-ui
**Areas discussed:** Recipe list display, Recipe form design, Recipe content editor, Recipe preview layout
**Mode:** Auto (--auto flag — all recommended defaults selected)

---

## Recipe List Display

| Option | Description | Selected |
|--------|-------------|----------|
| Standard data table | Columns: Name, Industry, Status, Version, counts, Actions | ✓ |
| Card grid | Visual but less data-dense | |
| Simple list | Too sparse for admin | |

**User's choice:** [auto] Standard data table (recommended default)

| Option | Description | Selected |
|--------|-------------|----------|
| Colored badge (green/gray) | Clear visual status | ✓ |
| Text-only status | Too subtle | |
| Icon-based | Ambiguous without label | |

**User's choice:** [auto] Colored badge (recommended default)

| Option | Description | Selected |
|--------|-------------|----------|
| Show all with filter toggle | Admins need visibility into deactivated | ✓ |
| Active only | Hides useful info | |
| Separate tabs | Overbuilt for recipe count | |

**User's choice:** [auto] Show all with status filter toggle (recommended default)

---

## Recipe Form Design

| Option | Description | Selected |
|--------|-------------|----------|
| Multi-section dedicated page | Metadata top, content below | ✓ |
| Modal form | Too cramped for recipe content | |
| Wizard/stepper | Overbuilt for this data model | |

**User's choice:** [auto] Multi-section form on dedicated page (recommended default)

| Option | Description | Selected |
|--------|-------------|----------|
| Inline validation (Phase 9 pattern) | Consistent UX | ✓ |
| Toast-only errors | Easy to miss | |

**User's choice:** [auto] Inline validation matching Phase 9 (recommended default)

| Option | Description | Selected |
|--------|-------------|----------|
| Auto-generated slug with override | Reduces errors | ✓ |
| Manual only | Error-prone | |
| Auto-generated, no override | Too restrictive | |

**User's choice:** [auto] Auto-generated with manual override (recommended default)

---

## Recipe Content Editor

| Option | Description | Selected |
|--------|-------------|----------|
| Inline editable lists | Add/remove/reorder per section | ✓ |
| JSON editor | Too technical for admin UI | |
| Separate pages per section | Too many navigations | |

**User's choice:** [auto] Inline editable lists (recommended default)

| Option | Description | Selected |
|--------|-------------|----------|
| Collapsible cards per section | Organized, scannable | ✓ |
| Always-open sections | Too long | |
| Tabbed sections | Hides content being edited | |

**User's choice:** [auto] Collapsible cards (recommended default)

---

## Recipe Preview

| Option | Description | Selected |
|--------|-------------|----------|
| Full page with tabs | More space for complex content | ✓ |
| Modal overlay | Too cramped | |
| Inline expansion in list | Limited space | |

**User's choice:** [auto] Full page at /admin/recipes/{id}/preview (recommended default)

| Option | Description | Selected |
|--------|-------------|----------|
| Read-only tabbed view | Organized by content type | ✓ |
| Single long page | Hard to navigate | |
| Accordion sections | Less scannable | |

**User's choice:** [auto] Read-only tabbed view (recommended default)

| Option | Description | Selected |
|--------|-------------|----------|
| Include sample leads tab | Useful for admin understanding | ✓ |
| Exclude sample leads | Missing context | |

**User's choice:** [auto] Show sample leads as separate tab (recommended default)

---

## Claude's Discretion

- Tailwind styling details (table rows, badges, form inputs)
- Loading states and notifications
- Pagination approach for recipe list
- Layout proportions for content editor

## Deferred Ideas

None — discussion stayed within phase scope.
