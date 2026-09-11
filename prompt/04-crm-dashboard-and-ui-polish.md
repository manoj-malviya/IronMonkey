# CRM Dashboard And UI Polish

## Context

The authenticated tenant dashboard currently shows only “Dashboard” and a welcome sentence. The sign-in page promises a real-time pipeline dashboard and automation engine, while the navigation exposes Leads, Contacts, Opportunities, Recipes, Users/Roles, Workflow Help, and System Configuration. The lead list works and includes search, stage filtering, actions, and a pipeline-board entry, but the experience is table-first and lacks operational feedback.

## Prompt

Build a tenant-focused CRM dashboard and apply a coherent usability pass to the existing authenticated UI.

### Requirements

- Replace the empty tenant dashboard with tenant-scoped metrics: open leads, leads by stage, conversion rate, opportunities by stage/value, overdue follow-ups, and recent activity. Use meaningful empty states when there is no data.
- Provide date-range filtering and a refresh control. Make loading, partial failure, stale data, and retry states explicit. Do not block the whole dashboard when one widget fails.
- Add actionable widgets: leads needing attention, recent conversions, and quick actions for creating a lead/contact/opportunity or configuring setup.
- Keep metrics computed from tenant data only. Add API contracts that aggregate server-side rather than loading all records into the browser.
- Improve list pages with consistent search/filter behavior, clear result counts, sortable columns where useful, pagination for growth, and preserved filter state when opening and returning from a detail page.
- Standardize confirmations, toast/banner feedback, validation summaries, disabled saving states, and retry behavior across lead, contact, opportunity, user, and configuration pages.
- Replace inert `#` footer links with real destinations or remove them until the destinations exist. Ensure every icon-only control has an accessible name and tooltip.
- Check responsive behavior at narrow widths: navigation drawer, tables, tabs, forms, modals, and pipeline board must not overlap or hide primary actions.
- Use the existing visual language, but reduce dense inline table actions and avoid adding decorative dashboard cards that do not support a workflow.

### Acceptance criteria

- A tenant Admin opening `/admin` can understand current pipeline health and reach the next action within one screen.
- Dashboard data is tenant-isolated, date-filterable, refreshable, and resilient to an individual widget failure.
- Empty tenants get useful setup/create actions rather than blank charts or zeros without context.
- The same feedback and responsive interaction patterns appear across the main CRM lists and configuration pages.
- Add API and UI tests for aggregation correctness, date boundaries, empty data, tenant isolation, partial failure, and authorization.

### Likely implementation surfaces

- `IronMonkey.Web/Components/Pages/Admin/Index.razor`
- `IronMonkey.Web/Components/Layout/AdminSidebar.razor`
- `IronMonkey.Web/Components/Pages/Admin/Leads/LeadList.razor`
- `IronMonkey.Web/Components/Pages/Admin/Contacts/ContactList.razor`
- `IronMonkey.Web/Components/Pages/Admin/Opportunities/OpportunityList.razor`
- Tenant-scoped dashboard query endpoints/services and tests