# Tenant Terminology, Localization and Branding

## Context

IronMonkey is sold as one product to many verticals. The data model is already tenant-configurable — stages, custom fields, workflow rules and routing all come from the tenant's own database — but the *language* of the product is not. Every screen, email, validation message and export says "Lead", "Opportunity", "Contact". A dealership calls them enquiries and deals; a university calls them applicants and admissions; a clinic calls them patients and referrals. A tenant that must mentally translate every label is being asked to adapt to the software rather than the other way round.

The same gap exists for formatting. `MoneyFormat` exists only because the app runs under the invariant culture, whose currency symbol is the placeholder `¤` — the workaround is to render bare numbers with `"N0"`. That is correct today precisely because the system does not know what currency a tenant uses. Dates have the same problem: a single stored `timestamptz` is rendered in server time, so a tenant in another timezone reads the wrong day on every "created" and "due" column.

`Tenant.ThemeSettings` exists as an unused nullable string.

## Prompt

Add a tenant-level presentation layer covering terminology, locale and branding, so one deployment can look and read like a product built for each vertical.

### Terminology

- Introduce a tenant-scoped terminology map for the core nouns the UI exposes: Lead, Contact, Opportunity, Task, Pipeline, Stage, Activity, and their plurals. Each entry needs a singular and a plural form; do not derive the plural by appending "s", since tenants use terms like "Enquiry/Enquiries" and words that do not pluralize regularly.
- Resolve terminology **once, server-side, per request or circuit** and expose it to Blazor through a single injected service. Do not scatter lookups through markup, and do not ship a dictionary of every term to the browser on every render.
- Apply it to page titles, navigation, table headers, buttons, confirmation dialogs, empty states, and validation messages. A term that appears only in one place is still worth routing through the service — the point is that no component hardcodes a noun.
- Fall back to the built-in default whenever a tenant has not overridden a term. A missing or blank override must never render an empty label.
- Do **not** rename routes, API contracts, permission strings, database columns, or entity types. Terminology is presentation only. `leads:read` stays `leads:read` even for a tenant that calls them Applicants — renaming the wire contract per tenant would make the API untestable and break every integration.

### Locale, currency and timezone

- Add tenant-level currency, locale and IANA timezone settings, with sensible defaults for a tenant that sets none.
- Replace the `MoneyFormat` workaround with formatting driven by the tenant's configured currency. Keep a safe fallback for an unconfigured tenant rather than reintroducing `¤`. Do not assume the tenant's currency matches the server culture.
- Render every stored UTC timestamp in the tenant's timezone, and label ambiguous values. Store UTC unchanged — this is a display concern, and rewriting stored instants would corrupt existing data and break the half-open date-range logic the dashboard depends on.
- Make date-range filters, dashboard presets and report boundaries evaluate against the tenant's timezone, so "today" means the tenant's today. Be explicit about how this interacts with `DashboardDateRange`'s existing half-open `[From, ToExclusive)` contract.

### Branding

- Give `Tenant.ThemeSettings` a real, validated schema: display name, logo reference, and a small constrained palette. Reject arbitrary CSS or script — this value reaches the rendered page, and an unvalidated string here is a stored-XSS vector across every user in the tenant.
- Apply branding to the authenticated shell and to tenant-facing artifacts such as web forms. Keep the platform-admin surface on the platform's own branding, so an operator can always tell which tenant they are looking at — especially while impersonating.
- Constrain logo uploads by type and size and serve them without trusting the client-supplied filename or content type.

### Recipes

- Extend `RecipeContentModel` so a recipe carries the terminology map and locale defaults for its vertical, and have provisioning copy them into the tenant like every other recipe artifact. The Automobile recipe should arrive saying "Enquiry", the Education recipe saying "Applicant", with no manual setup.
- Keep the existing rule that recipes are templates: a tenant must be free to edit every seeded term afterwards, and editing a tenant's terms must never write back to the platform catalog.
- Version the content model change so existing stored recipes without the new sections still deserialize. A recipe written before this change must keep provisioning successfully.

## Acceptance criteria

- A tenant configured with custom terminology sees those terms across navigation, lists, forms, dialogs and validation messages, while another tenant in the same deployment simultaneously sees its own.
- No term a tenant has not overridden renders blank, and no component hardcodes a core noun.
- Currency renders with the tenant's configured symbol and never as `¤`; an unconfigured tenant still gets a readable value.
- A tenant in a non-server timezone sees correct local dates, and "today" in a dashboard preset matches that tenant's today.
- Branding is applied to the tenant shell, rejects script and arbitrary CSS, and never obscures the impersonation banner.
- Provisioning from the Automobile and Education recipes yields vertical-appropriate terminology with no manual step, and recipes stored before this change still provision.
- Tests cover terminology fallback, plural overrides, cross-tenant isolation of terms, currency and timezone rendering, branding validation/rejection, and recipe backward compatibility.

## Likely implementation surfaces

- `IronMonkey.Data/Entities/Tenant.cs` and a tenant-scoped terminology/settings entity
- `IronMonkey.Data/RecipeContent/RecipeContentModel.cs` and `RecipeContentValidator`
- Tenant provisioning and recipe application services
- A terminology/format resolution service in `IronMonkey.Web` and `IronMonkey.ApiService`
- `IronMonkey.Web/Components/Layout/` and the Admin pages under `IronMonkey.Web/Components/Pages/Admin/`
- Existing `MoneyFormat` helper and `DashboardDateRange`
- Tests in `IronMonkey.Tests`

Terminology is a presentation layer over a stable contract. Do not let it leak into routes, permissions, or storage.
