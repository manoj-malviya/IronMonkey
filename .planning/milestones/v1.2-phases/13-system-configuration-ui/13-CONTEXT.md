# Phase 13: System Configuration UI - Context

**Gathered:** 2026-04-01
**Status:** Ready for planning

<domain>
## Phase Boundary

Build a single admin configuration page at `/admin/configuration` with 4 tabs: Pipeline Stages, Custom Fields, Routing, and Workflow Rules. All tabs use inline editing (edit-in-place within lists, no dedicated create/edit pages). This is the last phase of v1.2 Admin UI milestone.

</domain>

<decisions>
## Implementation Decisions

### Page Organization
- **D-01:** Single page at `/admin/configuration` with 4 tabs: Pipeline Stages, Custom Fields, Routing, Workflow Rules
- **D-02:** Client-side tab switching (no navigation) — follows TenantManagement and RecipePreview tabbed pattern
- **D-03:** Default tab is "Pipeline Stages" (most commonly configured)
- **D-04:** All editing is inline — no separate create/edit pages for any config items

### Pipeline Stages Tab (CFUI-01)
- **D-05:** List of stages showing Name, Order, StageType (Entry/Active/ClosedWon/ClosedLost), Actions
- **D-06:** Up/down arrow buttons per row for reordering — saves updated order via PUT endpoint
- **D-07:** "+ Add Stage" button appends an editable row at bottom — inline Name input + StageType dropdown, save button
- **D-08:** Click existing row to make it editable (Name and StageType) — save/cancel buttons appear
- **D-09:** Delete button per row with confirmation — removes stage
- **D-10:** Empty state: "No pipeline stages configured. Add your first stage to get started."

### Custom Fields Tab (CFUI-02)
- **D-11:** List of fields showing Name, Type (Text/Number/Date/Select/Boolean), IsRequired badge, Options (for Select type), Actions
- **D-12:** "+ Add Field" button appends editable row — Name input, Type dropdown, IsRequired toggle, Options input (comma-separated, shown only for Select type)
- **D-13:** Click existing row to edit — same fields as create
- **D-14:** Delete button per row with confirmation
- **D-15:** Empty state: "No custom fields defined. Add fields to capture tenant-specific lead data."

### Routing Configuration Tab (CFUI-03)
- **D-16:** Mode selector at top: radio buttons for "Round-Robin" or "Territory-Based"
- **D-17:** Form fields change based on selected mode
- **D-18:** Round-Robin mode: just the mode selection (no additional config needed — backend handles assignment)
- **D-19:** Territory mode: JSON config textarea for territory rules (matches ConfigureRoutingEndpoint contract)
- **D-20:** Single "Save Configuration" button at bottom
- **D-21:** Current config loaded from GET /api/routing-config on tab open

### Workflow Rules Tab (CFUI-04)
- **D-22:** Table columns: Name/Description, TriggerType, ActionType, IsActive (toggle), Actions (Edit/Delete)
- **D-23:** Structured form inputs for rule editing: dropdowns for TriggerType and ActionType, text inputs for Conditions JSON and ActionConfig JSON
- **D-24:** "+ Add Rule" button opens inline editable section (not a new page)
- **D-25:** Click existing rule row to expand editable form below it — save/cancel buttons
- **D-26:** IsActive toggle per rule — immediate POST to toggle endpoint
- **D-27:** Delete button with confirmation
- **D-28:** Empty state: "No workflow rules configured. Create rules to automate your lead pipeline."

### Claude's Discretion
- Exact Tailwind styling for inline edit rows, toggle switches, confirmation dialogs
- Loading state approach for each tab
- Toast/notification for save success/failure
- Animation for expanding/collapsing inline edit rows
- Tab icons (if any)
- Exact JSON textarea sizing and placeholder text

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Backend API Endpoints — Pipeline Stages
- `IronMonkey.ApiService/Features/Leads/PipelineStages/ListPipelineStagesEndpoint.cs` — GET `/api/pipeline-stages`, tenant-scoped
- `IronMonkey.ApiService/Features/Leads/PipelineStages/CreatePipelineStageEndpoint.cs` — POST `/api/pipeline-stages`
- `IronMonkey.ApiService/Features/Leads/PipelineStages/UpdatePipelineStageEndpoint.cs` — PUT `/api/pipeline-stages/{id}`

### Backend API Endpoints — Custom Fields
- `IronMonkey.ApiService/Features/Leads/CustomFields/ListCustomFieldsEndpoint.cs` — GET `/api/custom-fields`, tenant-scoped
- `IronMonkey.ApiService/Features/Leads/CustomFields/CreateCustomFieldEndpoint.cs` — POST `/api/custom-fields`

### Backend API Endpoints — Routing
- `IronMonkey.ApiService/Features/Leads/Pipeline/Routing/GetRoutingConfigEndpoint.cs` — GET `/api/routing-config`, tenant-scoped
- `IronMonkey.ApiService/Features/Leads/Pipeline/Routing/ConfigureRoutingEndpoint.cs` — POST `/api/routing-config`

### Backend API Endpoints — Workflow Rules
- `IronMonkey.ApiService/Features/Leads/Workflow/Rules/ListWorkflowRulesEndpoint.cs` — GET `/api/workflow-rules`, tenant-scoped
- `IronMonkey.ApiService/Features/Leads/Workflow/Rules/CreateWorkflowRuleEndpoint.cs` — POST `/api/workflow-rules`
- `IronMonkey.ApiService/Features/Leads/Workflow/Rules/UpdateWorkflowRuleEndpoint.cs` — PUT `/api/workflow-rules/{id}` + POST `/api/workflow-rules/{id}/toggle`

### Data Model
- `IronMonkey.Data/Entities/PipelineStage.cs` — Name, Order, StageType, TenantId
- `IronMonkey.Data/Entities/CustomFieldDefinition.cs` — Name, FieldType, IsRequired, Options (JSONB List<string>)
- `IronMonkey.Data/Entities/RoutingConfig.cs` — Mode, Configuration (JSONB)
- `IronMonkey.Data/Entities/WorkflowRule.cs` — Name, TriggerType, Conditions (JSONB), ActionType, ActionConfig (JSONB), IsActive

### Phase 9-12 UI Patterns (must follow)
- `IronMonkey.Web/Components/Pages/Admin/Tenants/TenantManagement.razor` — Tabbed page pattern
- `IronMonkey.Web/Components/Pages/Admin/Users/UserList.razor` — Data table with action buttons, modal confirmation
- `IronMonkey.Web/Components/Pages/Admin/Recipes/RecipeList.razor` — Table + filter toggle pattern
- `IronMonkey.Web/HttpHandlers/BearerTokenHandler.cs` — Auto-injects Bearer token on AdminApi calls

### Project Configuration
- `.planning/REQUIREMENTS.md` — CFUI-01 through CFUI-04 (this phase's requirements)
- `.planning/PROJECT.md` — Key decisions: native Razor + Tailwind only, no component libraries

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `BearerTokenHandler` + named HttpClient `"AdminApi"` — all API calls from Blazor pages
- TenantManagement.razor tabbed view pattern — client-side tab switching with active styling
- UserList.razor modal confirmation pattern — for delete confirmations
- RecipeList.razor data table pattern — loading/empty states

### Established Patterns
- **API calls**: `IHttpClientFactory.CreateClient("AdminApi")` → `GetFromJsonAsync<T>` / `PostAsJsonAsync` / `PutAsJsonAsync` / `DeleteAsync`
- **Page structure**: `@page "/admin/..."` + `@attribute [Authorize]` + `@inject IHttpClientFactory HttpClientFactory`
- **Styling**: Tailwind utility classes, slate/indigo, Heroicons

### Integration Points
- All 4 backend endpoint groups exist and are tested — this is purely UI work
- **Gap: No "delete pipeline stage" endpoint** — CFUI-01 requires delete capability. Need new DELETE endpoint or use soft delete
- **Gap: No "update custom field" endpoint** — CFUI-02 requires edit. Only create and list exist
- **Gap: No "delete custom field" endpoint** — CFUI-02 requires delete
- **Gap: No "delete workflow rule" endpoint** — CFUI-04 requires delete
- AdminSidebar has a "Configuration" section — routing is ready

</code_context>

<specifics>
## Specific Ideas

- This is the last phase of v1.2 — all admin UI will be complete after this
- 4 backend endpoint gaps need to be filled (delete stage, update/delete field, delete rule)
- Inline editing is a new pattern not used in prior phases — will need careful state management for which row is being edited
- Routing config is the simplest tab — just mode selector + save
- Workflow rules are the most complex — TriggerType/ActionType dropdowns + JSON textareas for conditions/config

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 13-system-configuration-ui*
*Context gathered: 2026-04-01*
