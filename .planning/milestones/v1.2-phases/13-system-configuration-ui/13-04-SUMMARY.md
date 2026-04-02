---
phase: 13-system-configuration-ui
plan: "04"
subsystem: web-ui
tags: [blazor, workflow-rules, system-configuration, admin-sidebar, tailwind]
dependency_graph:
  requires: [13-03]
  provides: [CFUI-04, workflow-rules-tab, admin-configuration-nav]
  affects: [IronMonkey.Web]
tech_stack:
  added: []
  patterns: [inline-edit-row, toggle-active-pattern, lazy-tab-load, shared-delete-modal]
key_files:
  created: []
  modified:
    - IronMonkey.Web/Components/Pages/Admin/Configuration/SystemConfiguration.razor
    - IronMonkey.Web/Components/Layout/AdminSidebar.razor
decisions:
  - "Consolidated 4 separate sidebar nav links (stages/fields/routing/workflows) into single /admin/configuration link — all 4 features live as tabs in SystemConfiguration.razor"
  - "Workflow Rules inline edit row placed as sibling <tr> inside @foreach loop, matching Custom Fields pattern from plan 13-03"
  - "ToggleRuleActiveAsync uses PostAsync with StringContent(string.Empty) to ensure Content-Type header is set — PostAsJsonAsync with null omits it"
metrics:
  duration: "~8 minutes"
  completed_date: "2026-04-01"
  tasks_completed: 2
  files_modified: 2
---

# Phase 13 Plan 04: Workflow Rules Tab and Sidebar Nav Summary

Completed the System Configuration UI with the Workflow Rules tab (full inline CRUD + IsActive toggle) and unified the AdminSidebar Configuration nav to point to `/admin/configuration`.

## Tasks Completed

### Task 1: Workflow Rules tab — full inline CRUD with trigger/action dropdowns and IsActive toggle
**Commit:** e923e1a

Replaced the "Coming soon" placeholder in the Workflow Rules tab with a complete implementation:

- `RuleItem` DTO class matching `ListWorkflowRulesEndpoint` response (`Id`, `Name`, `Trigger`, `ConditionJson`, `ActionJson`, `IsActive`)
- Table with columns: Name, Trigger, Active (toggle), Actions
- IsActive toggle: immediate `POST /api/workflow-rules/{id}/toggle` with no confirmation dialog; `StringContent(string.Empty)` used to preserve Content-Type header
- Inline edit row: expands below the rule row when Edit is clicked; two-column Name/Trigger grid, plus JSON textareas for Condition and Action
- Inline add form: shown below the table when `_creatingRule = true`
- Lazy load: `SetTab()` extended to call `LoadRulesAsync()` on first Workflow Rules tab visit
- Delete: uses shared `_showDeleteConfirm` modal; `ConfirmDeleteAsync()` extended with `"rule"` branch

### Task 2: AdminSidebar.razor — add Configuration nav link
**Commit:** 96a4d10

Replaced 4 stale per-feature links (`/admin/stages`, `/admin/fields`, `/admin/routing`, `/admin/workflows`) with a single "System Configuration" NavLink pointing to `/admin/configuration`. This matches the actual page structure where all 4 tabs live at one URL.

## Verification

```
Build: 0 errors, 2 warnings (pre-existing)
grep admin/configuration AdminSidebar.razor: 1 match
grep "Coming soon" SystemConfiguration.razor: 0 matches
grep -c "workflow-rules|pipeline-stages|custom-fields|routing-config": 19 matches
```

## Deviations from Plan

### Auto-fixed Issues

None — plan executed exactly as written, with one deliberate scope improvement:

**1. [Rule 1 - Scope Improvement] Removed stale per-feature sidebar links**
- **Found during:** Task 2
- **Issue:** AdminSidebar had 4 separate nav links (`/admin/stages`, `/admin/fields`, `/admin/routing`, `/admin/workflows`) pointing to routes that don't exist as standalone pages — they are tabs in SystemConfiguration.razor at `/admin/configuration`
- **Fix:** Consolidated to single `System Configuration` link at `/admin/configuration`
- **Files modified:** `IronMonkey.Web/Components/Layout/AdminSidebar.razor`
- **Commit:** 96a4d10

## Known Stubs

None — all four tabs are fully wired to real API endpoints. No placeholder data.

## Self-Check: PASSED

Files exist:
- [x] `IronMonkey.Web/Components/Pages/Admin/Configuration/SystemConfiguration.razor`
- [x] `IronMonkey.Web/Components/Layout/AdminSidebar.razor`

Commits exist:
- [x] e923e1a — feat(13-04): implement Workflow Rules tab
- [x] 96a4d10 — feat(13-04): update AdminSidebar.razor
