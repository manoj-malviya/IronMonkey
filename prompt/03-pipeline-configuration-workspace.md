# Pipeline And Custom Field Configuration Workspace

## Context

`/admin/configuration` currently combines Pipeline Stages, Custom Fields, Routing, and Workflow Rules in one tabbed page. Stages can be created, renamed, reordered, and deleted. Custom fields support Lead or Contact scope, several types, required state, and comma-separated options. Routing exposes raw JSON for territory rules. These capabilities need safer domain behavior and a more usable configuration workspace.

## Prompt

Redesign tenant configuration as a resilient, discoverable workspace while preserving the existing API contracts where practical.

### Requirements

- Treat stage identity and ordering as first-class data. Allow active/inactive state, prevent duplicate names, and explain the impact before deleting or deactivating a stage that has records assigned to it.
- Replace inline stage editing with a focused dialog or side panel. Support drag-and-drop ordering with keyboard-accessible move controls as a fallback. Save ordering atomically and show unsaved/saving/success/error states.
- Make custom-field creation a real form with a field label, internal key, applies-to scope, type, required toggle, help text, default value, and type-specific options. Use repeatable option inputs for dropdown and multi-select rather than comma-separated text.
- Prevent changing a field type or scope when existing values would become invalid unless the Admin explicitly chooses a migration strategy. Show usage counts and a clear warning before delete/archive.
- Add field preview so the Admin sees how a field will render on lead/contact forms. Ensure required custom fields are validated consistently on create and edit forms.
- Replace raw territory JSON with a structured rule editor for territory name plus states/regions and assigned users or teams. Keep an advanced JSON view only as an optional escape hatch with schema validation and readable errors.
- Add stable tab URLs or query parameters so a refresh and deep link preserve the selected configuration area. Add concise descriptions and links to relevant help without repeating large explanatory banners.
- Make tables responsive: preserve action access on mobile, truncate long option lists with a details affordance, and avoid horizontal scrolling for the primary task.
- Add audit metadata or a recent-change summary for configuration changes when the existing audit model supports it.

### Acceptance criteria

- An Admin can configure stages and custom fields without losing data, creating duplicates, or accidentally invalidating records.
- Deleting/deactivating a referenced stage or field presents impact counts and a safe resolution path.
- Lead and contact forms render the configured fields with correct types, required validation, defaults, and stored values.
- Routing rules can be authored without hand-writing JSON, and invalid advanced JSON cannot be saved.
- Configuration survives refresh/deep link and works at desktop and mobile widths.
- Add tests for duplicate names, referenced-item protection, ordering concurrency, field validation, option changes, routing validation, and tenant isolation.

### Likely implementation surfaces

- `IronMonkey.Web/Components/Pages/Admin/Configuration/SystemConfiguration.razor`
- `IronMonkey.Web/Components/Shared/CustomFieldsEditor.razor`
- `IronMonkey.Web/Components/Pages/Admin/Leads/LeadForm.razor`
- `IronMonkey.Web/Components/Pages/Admin/Contacts/ContactForm.razor`
- Pipeline-stage, custom-field, and routing endpoints/services/entities in `IronMonkey.ApiService` and `IronMonkey.Data`