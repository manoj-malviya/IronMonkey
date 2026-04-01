# Phase 11: Tenant Management UI - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-04-01
**Phase:** 11-tenant-management-ui
**Areas discussed:** Page organization, Signup detail presentation, Approve/provision flow, Tenant list columns
**Mode:** Auto (--auto flag — all areas auto-selected with recommended defaults)

---

## Page Organization

| Option | Description | Selected |
|--------|-------------|----------|
| Two tabs on one page | Single page at /admin/tenants with "Tenants" and "Signup Requests" tabs | ✓ |
| Separate pages | Dedicated /admin/tenants and /admin/signup-requests pages | |
| Combined single list | One table showing both tenants and signup requests with type column | |

**User's choice:** [auto] Two tabs on one page (recommended — follows RecipePreview tabbed pattern, keeps related admin tasks together)
**Notes:** Matches the tabbed pattern established in Phase 10's RecipePreview.razor

---

## Signup Detail Presentation

| Option | Description | Selected |
|--------|-------------|----------|
| Expandable row | Click row to expand and show full signup details inline | ✓ |
| Side panel/flyout | Slide-out panel from right side showing details | |
| Dedicated page | Navigate to /admin/signup-requests/{id} for detail view | |

**User's choice:** [auto] Expandable row (recommended — keeps admin in context, avoids navigation for quick review)
**Notes:** Signup requests have limited fields — expandable row sufficient without navigation overhead

---

## Approve/Provision Flow

| Option | Description | Selected |
|--------|-------------|----------|
| Auto-provision on approval | Single "Approve" action chains approve + provision API calls | ✓ |
| Two-step process | Approve first, then separate "Provision" button | |
| Background provisioning | Approve triggers async provisioning via Hangfire job | |

**User's choice:** [auto] Auto-provision on approval (recommended — reduces admin friction, chains POST approve → POST provision sequentially)
**Notes:** Fallback: if provisioning fails post-approval, show error and expose retry "Provision" button on Approved-but-unprovisioned entries

---

## Tenant List Columns

| Option | Description | Selected |
|--------|-------------|----------|
| Full operational view | Name, Status, SubscriptionPlan, Provisioned, Applied Recipe, Created Date | ✓ |
| Minimal view | Name, Status, Created Date only | |
| Extended view | All above + Slug, Connection String indicator, Theme Settings | |

**User's choice:** [auto] Full operational view (recommended — covers TNUI-01 requirements with useful operational context)
**Notes:** Connection string and theme settings are internal — not useful for admin overview

---

## Claude's Discretion

- Exact Tailwind styling for tables, badges, tabs
- Loading skeleton/spinner approach
- Toast/notification style for success/error messages
- Tab component implementation (CSS-only vs Blazor component)
- Expandable row animation

## Deferred Ideas

None — discussion stayed within phase scope.
