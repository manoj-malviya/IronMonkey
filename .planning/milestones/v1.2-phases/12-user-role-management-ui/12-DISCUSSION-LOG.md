# Phase 12: User & Role Management UI - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-04-01
**Phase:** 12-user-role-management-ui
**Areas discussed:** User list page, Create user flow, Edit user flow, Deactivate behavior

---

## User List Page

| Option | Description | Selected |
|--------|-------------|----------|
| Dedicated page | /admin/users — standalone page following RecipeList pattern | ✓ |
| Tab on tenant page | Add 'Users' tab to TenantManagement.razor | |
| Dedicated with role filter | /admin/users with role dropdown filter | |

**User's choice:** Dedicated page
**Notes:** Users are a primary admin concern with CRUD, justifies own page

| Option | Description | Selected |
|--------|-------------|----------|
| Name, Email, Role, Status, Created | Core info matching USUI-01 | ✓ |
| Same + Actions column | Plus explicit Edit/Deactivate buttons | |
| Minimal: Name, Email, Role | Compact view | |

**User's choice:** Name, Email, Role, Status, Created
**Notes:** Actions implied via row buttons following established pattern

---

## Create User Flow

| Option | Description | Selected |
|--------|-------------|----------|
| Dedicated page | /admin/users/create following RecipeCreate pattern | ✓ |
| Modal on list page | Dialog overlay | |
| Slide-out panel | Side panel on list page | |

**User's choice:** Dedicated page

| Option | Description | Selected |
|--------|-------------|----------|
| Admin enters password | Admin types password for user | |
| Auto-generate and display | System generates, displays once | ✓ |
| Email invitation link | User sets own password via email | |

**User's choice:** Auto-generate and display
**Notes:** More secure, admin shares generated password. No password field in form.

---

## Edit User Flow

| Option | Description | Selected |
|--------|-------------|----------|
| Dedicated page | /admin/users/{id}/edit following RecipeEdit pattern | ✓ |
| Modal on list page | Click Edit opens modal | |
| Inline edit on list | Cells become editable in-place | |

**User's choice:** Dedicated page

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, with reset button | Reset Password button generates new password, displays once | ✓ |
| No password management | Defer to future self-service feature | |

**User's choice:** Yes, with reset button
**Notes:** Separate from form save — independent action on edit page

---

## Deactivate Behavior

| Option | Description | Selected |
|--------|-------------|----------|
| Confirmation modal | Modal confirms with user name, sets IsDeleted=true | ✓ |
| Inline toggle | Status toggle switch, no confirmation | |
| Deactivate on edit page | Only available from edit page | |

**User's choice:** Confirmation modal

| Option | Description | Selected |
|--------|-------------|----------|
| Toggle filter | Checkbox "Show Deactivated Users", hidden by default | ✓ |
| Always show all | All users visible with status badge | |
| Separate tab | Active/Deactivated tabs | |

**User's choice:** Toggle filter
**Notes:** Same pattern as RecipeList's "Show Deactivated Recipes"

---

## Claude's Discretion

- Tailwind styling, loading states, toast notifications
- Password display UX (modal vs banner, copy button)
- Role dropdown styling

## Deferred Ideas

None — discussion stayed within phase scope.
