# Custom Objects and Record Relationships

## Context

The system is domain-agnostic in its *fields* but not in its *entities*. A tenant can add custom fields to Lead, Contact and Opportunity, and nothing else. Every vertical this product targets has at least one first-class record the fixed model cannot express: a dealership tracks Vehicles and Test Drives, a university tracks Programs and Applications, a clinic tracks Appointments, a broker tracks Policies and Renewals. Today the only way to represent them is to flatten them into custom fields on a Lead — which fails as soon as one lead relates to several of them, or the record outlives the lead.

Relationships are equally fixed. `Opportunity` has one `ContactId`; `Lead` converts into a contact and an opportunity through dedicated nullable columns; there is no Account/Company record at all, despite `Lead.ConvertedAccountId` referring to one. Nothing lets a tenant say "this contact belongs to that company" or "this opportunity involves these three people".

## Prompt

Let a tenant define its own record types and the relationships between them, without code changes and without weakening tenant isolation.

### Custom object definitions

- Add a tenant-scoped object-definition model: name and plural name, key, icon, and the fields it carries. Reuse `CustomFieldDefinition` and its `AppliesTo` scoping rather than inventing a parallel field system — the binder, validation, archival and migration-strategy rules already solved there must apply unchanged to custom objects.
- Store custom object records in a tenant table with a JSONB value bag, following the existing `CustomFieldValues` pattern. Do not create a physical table per tenant-defined object: per-tenant DDL at runtime turns every schema edit into a migration hazard across every tenant database, and the codebase already has a working JSONB convention.
- Keep values keyed by field definition id, never by label or key — the existing invariant, and the one that makes renaming a field safe.
- Any raw SQL touching the JSONB bag must filter `TenantId` explicitly, because the global query filters do not apply to it, and must use `jsonb_exists(col, @key)` rather than `?`, which Npgsql parses as a parameter placeholder. Both traps are already documented for `custom_field_values`.
- Enforce per-tenant limits on object count, field count and record count, and make the limits configurable per plan. An unbounded definition surface is a denial-of-service vector against a shared database server.

### Relationships

- Support one-to-many and many-to-many relationships between built-in entities and custom objects, defined as tenant data rather than code.
- Define referential behaviour explicitly for each relationship: what deletion of one side does to the other, and whether the link is required. Reuse the existing impact-report pattern — `GET .../{id}/impact` — so nothing referenced is ever deleted silently, exactly as pipeline stages and custom fields already behave.
- Add a proper Account/Company record, or state clearly why it remains a custom object, and reconcile it with the existing `Lead.ConvertedAccountId` column, which currently points at nothing.
- Show related records on the detail pages of both sides, with paging. A related list must never load an unbounded set.

### Surfacing

- Generate list, detail and edit surfaces for custom objects from their definitions, honouring the field types, archival rules and default-value handling that `CustomFieldValueBinder` already implements. Do not bypass the binder.
- Make custom objects available to search, workflow conditions and actions, routing, and reporting — or state explicitly and deliberately which of those are out of scope for this change. A record type nothing can automate against is a dead end.
- Include custom object definitions and their relationships in `RecipeContentModel`, so a vertical recipe can ship "Vehicle" or "Program" preconfigured. Recipes stored before this change must still provision.
- Custom object definitions are tenant data, not platform catalog data. A tenant editing its own objects must never write into the central database or into another tenant's schema — the same boundary that separates `IndustryRecipe` from provisioned tenant rows.

## Acceptance criteria

- A tenant Admin can define a new object with fields, create records, and relate them to leads, contacts and opportunities, with no code deployment.
- Relationships enforce their declared referential behaviour, and a delete that would orphan records is reported through an impact check before it happens.
- Custom object values are keyed by definition id, survive a field rename, and honour archived-field and default-value rules through the existing binder.
- Raw SQL over the JSONB bag filters `TenantId` and cannot read or write another tenant's rows.
- Per-tenant limits are enforced and produce a clear error rather than a database failure.
- A recipe can seed a vertical-specific object, and recipes stored before this change still provision.
- Tests cover definition CRUD, record CRUD, relationship integrity, impact reporting, cross-tenant isolation including the raw-SQL paths, limit enforcement, and recipe compatibility.

## Likely implementation surfaces

- New object-definition, record and relationship entities in `IronMonkey.Data/Entities/` plus a tenant migration
- `IronMonkey.Data/Entities/CustomFieldDefinition.cs`, `CustomFieldValues.cs`
- `IronMonkey.ApiService/Features/CustomFields/CustomFieldValueBinder.cs`
- `IronMonkey.ApiService/Features/Configuration/` and `IConfigurationUsageService`
- `IronMonkey.Data/RecipeContent/RecipeContentModel.cs`
- New Blazor pages under `IronMonkey.Web/Components/Pages/Admin/`
- Tests in `IronMonkey.Tests`

This is the single largest lever on multi-industry fit. Build the generic mechanism; do not hardcode Vehicle, Program or Policy anywhere in the engine.
