# Tenant Onboarding Experience

## Context

IronMonkey is a database-per-tenant CRM. A tenant can be approved and provisioned, then an Admin can manage users, pipeline stages, custom fields, routing, and workflow rules. The current authenticated dashboard only renders a heading and welcome text for tenant users. The existing platform tenant-management flow must remain separate from the tenant Admin experience.

## Prompt

Implement a guided first-run onboarding experience for a provisioned tenant Admin.

### Requirements

- Detect whether the current tenant has completed the essential setup steps: create at least one additional team member, configure pipeline stages, configure at least one custom field, and review lead-routing settings.
- Show a compact onboarding checklist on `/admin` only while one or more steps remain incomplete. Do not show platform-wide counts or central-database data to tenant users.
- Each checklist item must link to the owning page and show a clear completion state. The Admin must be able to dismiss the checklist, with a way to reopen it from the dashboard.
- Add an explicit tenant context summary to the dashboard: tenant/company name, current user, role, and a link to tenant settings/profile if that capability exists. Never expose another tenant's information.
- Add a setup completion percentage or equivalent progress indicator, but keep it secondary to real CRM work once setup is complete.
- Define a small server-side setup-status contract instead of inferring state only in the browser. It must be tenant-scoped, authorization-protected, and safe when data is empty.
- Preserve existing routes and `AdminApiClient` token handling. Fetch after the first interactive render so prerendering does not cause a false unauthorized state.

### Acceptance criteria

- A new provisioned tenant sees the checklist with four incomplete items and useful empty states.
- Adding a team member, stage, and custom field updates the relevant checklist state after refresh without leaking data across tenants.
- A tenant with all required setup complete sees a useful CRM dashboard rather than a permanently empty welcome page.
- A dismissed checklist stays dismissed for that tenant/user and can be reopened.
- Add focused API and component/integration tests for empty, partial, complete, unauthorized, and cross-tenant cases.

### Likely implementation surfaces

- `IronMonkey.Web/Components/Pages/Admin/Index.razor`
- `IronMonkey.Web/Components/Layout/AdminSidebar.razor`
- Existing tenant-scoped API endpoints and services in `IronMonkey.ApiService`
- A tenant-scoped setup-status DTO/service and tests in `IronMonkey.Tests`

Do not turn this into a marketing landing page. The dashboard should become an operational workspace after setup.