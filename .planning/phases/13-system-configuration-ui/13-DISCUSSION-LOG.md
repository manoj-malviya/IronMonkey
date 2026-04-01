# Phase 13: System Configuration UI - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.

**Date:** 2026-04-01
**Phase:** 13-system-configuration-ui
**Areas discussed:** Page organization, Pipeline stages UX, Workflow rules editor, Routing config layout

---

## Page Organization

| Option | Description | Selected |
|--------|-------------|----------|
| Single page with tabs | /admin/configuration with 4 tabs | ✓ |
| Separate pages per area | 4 separate sidebar nav items | |
| Two pages: pipeline + rules | Grouped config pages | |

**User's choice:** Single page with tabs

| Option | Description | Selected |
|--------|-------------|----------|
| Inline editing | Edit directly in table/list, save in-place | ✓ |
| Modal forms | Click Edit opens modal | |
| Dedicated pages | Full page forms per config item | |

**User's choice:** Inline editing

---

## Pipeline Stages UX

| Option | Description | Selected |
|--------|-------------|----------|
| Up/down arrow buttons | Simple buttons per row for reordering | ✓ |
| Drag and drop | JS interop library required | |
| Numeric order input | Type desired order number | |

**User's choice:** Up/down arrow buttons

| Option | Description | Selected |
|--------|-------------|----------|
| Inline add row | + Add Stage appends editable row | ✓ |
| Modal form | Click Add opens modal | |
| Top form + list below | Persistent form above list | |

**User's choice:** Inline add row

---

## Workflow Rules Editor

| Option | Description | Selected |
|--------|-------------|----------|
| Structured form inputs | Dropdowns for types, text for JSON | ✓ |
| Full visual builder | Drag-drop rule builder | |
| Raw JSON editor | Single JSON textarea | |

**User's choice:** Structured form inputs

| Option | Description | Selected |
|--------|-------------|----------|
| Summary columns | Name, TriggerType, ActionType, IsActive toggle | ✓ |
| Card-based layout | Each rule as a card | |
| Compact table | Minimal info in list | |

**User's choice:** Summary columns

---

## Routing Config Layout

| Option | Description | Selected |
|--------|-------------|----------|
| Mode selector + form | Radio buttons for mode, fields change | ✓ |
| Side-by-side panels | Both modes always visible | |
| Accordion sections | Collapsible sections per mode | |

**User's choice:** Mode selector + form

| Option | Description | Selected |
|--------|-------------|----------|
| Mode + JSON config | Mode selector + territory JSON textarea | ✓ |
| Visual territory builder | Drag-drop assignment | |
| You decide | Claude picks | |

**User's choice:** Mode + JSON config

---

## Claude's Discretion

- Tailwind styling, loading states, toasts, animations
- Tab icons, JSON textarea sizing

## Deferred Ideas

None.
