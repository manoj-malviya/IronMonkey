# Phase 10: Recipe Management UI - Context

**Gathered:** 2026-04-01
**Status:** Ready for planning

<domain>
## Phase Boundary

Build admin pages for full recipe lifecycle management: list all recipes in a data table, create new recipes via a multi-section form, edit existing recipes, preview recipe content in a read-only tabbed view, and deactivate recipes. All pages live under `/admin/recipes` within the authenticated admin shell from Phase 9.

</domain>

<decisions>
## Implementation Decisions

### Recipe List Page
- **D-01:** Standard data table with columns: Name, Industry (slug), Status, Version, Stage/Field/Rule/Role counts, Actions
- **D-02:** Status displayed as colored badge — green for active, gray/muted for inactive
- **D-03:** Show both active and deactivated recipes with a status filter toggle
- **D-04:** Empty state: centered message with "Create your first recipe" CTA button
- **D-05:** Actions column: Edit, Preview, Deactivate buttons per row

### Recipe Form (Create/Edit)
- **D-06:** Multi-section form on dedicated page — metadata at top (name, description, industry slug, icon identifier, isBlank toggle), content sections below
- **D-07:** Inline validation matching Phase 9 pattern — errors below each field + summary banner at top
- **D-08:** Industry slug auto-generated from name with manual override option
- **D-09:** Single "Save" button at bottom — saves entire recipe atomically via POST (create) or PUT (edit)
- **D-10:** Create page at `/admin/recipes/create`, Edit page at `/admin/recipes/{id}/edit`

### Recipe Content Editor (within Create/Edit form)
- **D-11:** Pipeline stages: inline editable list with add/remove/reorder — fields: Name, Order, StageType (Entry/Active/ClosedWon/ClosedLost)
- **D-12:** Custom fields: inline editable list with add/remove — fields: Name, Type, IsRequired toggle, Options (for select types)
- **D-13:** Workflow rules: inline editable list with add/remove — fields: TriggerType, Conditions (JSON), ActionType, ActionConfig
- **D-14:** Roles: simple list with add/remove — fields: Name, Description
- **D-15:** Each content section is a collapsible card within the form

### Recipe Preview
- **D-16:** Full page at `/admin/recipes/{id}/preview` — more space for complex nested content
- **D-17:** Read-only tabbed view with tabs: Stages, Fields, Rules, Roles, Sample Leads
- **D-18:** Each tab shows clean tables matching the RecipeContentModel structure
- **D-19:** "Back to List" and "Edit" navigation buttons at top

### Claude's Discretion
- Exact Tailwind styling for table rows, badges, form inputs (maintain consistency with Phase 9 palette)
- Loading skeleton/spinner approach while API calls complete
- Pagination vs. scroll for the recipe list (likely few enough to show all)
- Toast/notification style for success messages after save/deactivate
- Exact layout proportions for the content editor sections

### Folded Todos
None.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Backend Recipe API Endpoints
- `IronMonkey.ApiService/Features/Recipes/RecipeListEndpoint.cs` — GET `/api/recipes`, returns list with counts (AllowAnonymous)
- `IronMonkey.ApiService/Features/Recipes/RecipePreviewEndpoint.cs` — GET `/api/recipes/{id}`, returns full RecipeContentModel (AllowAnonymous)
- `IronMonkey.ApiService/Features/Recipes/CreateRecipeEndpoint.cs` — POST `/api/recipes`, requires auth, validates name/slug/content
- `IronMonkey.ApiService/Features/Recipes/UpdateRecipeEndpoint.cs` — PUT `/api/recipes/{id}`, requires auth
- `IronMonkey.ApiService/Features/Recipes/DeactivateRecipeEndpoint.cs` — DELETE `/api/recipes/{id}`, requires auth

### Data Model
- `IronMonkey.Data/RecipeContent/RecipeContentModel.cs` — JSONB content: PipelineStages, CustomFields, WorkflowRules, Roles, SampleLeads with nested definitions

### Phase 9 UI Foundation (patterns to follow)
- `IronMonkey.Web/Components/Pages/Login.razor` — Form validation pattern, loading state, error handling
- `IronMonkey.Web/Components/Layout/AdminSidebar.razor` — Nav structure (recipes link at `/admin/recipes`)
- `IronMonkey.Web/Components/Layout/MainLayout.razor` — Layout shell wrapping all admin pages
- `IronMonkey.Web/HttpHandlers/BearerTokenHandler.cs` — Auto-injects Bearer token on AdminApi calls
- `IronMonkey.Web/Authentication/AdminAuthenticationStateProvider.cs` — Auth state management

### Project Configuration
- `.planning/REQUIREMENTS.md` — RCUI-01 through RCUI-05 (this phase's requirements)
- `.planning/PROJECT.md` — Key decisions: native Razor + Tailwind only, no component libraries

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `BearerTokenHandler` + named HttpClient `"AdminApi"` — all API calls from Blazor pages use this; just inject `IHttpClientFactory` and create `"AdminApi"` client
- Login.razor validation pattern — `EditForm` with `DataAnnotationsValidator` and inline `ValidationMessage` components
- AdminSidebar already links to `/admin/recipes` — routing is ready

### Established Patterns
- **API calls**: Use `IHttpClientFactory.CreateClient("AdminApi")` → `GetFromJsonAsync<T>` / `PostAsJsonAsync` / `PutAsJsonAsync` / `DeleteAsync`
- **Form validation**: Blazor `EditForm` + `DataAnnotationsValidator` + `ValidationMessage` per field
- **Page structure**: `@page "/admin/..."` + `@attribute [Authorize]` + `@inject IHttpClientFactory HttpClientFactory`
- **Styling**: Tailwind utility classes, slate/indigo color scheme, Heroicons for icons

### Integration Points
- Recipe list endpoint returns: Id, Name, Description, IconIdentifier, IndustrySlug, IsBlank, Version, StageCount, FieldCount, RuleCount, RoleCount
- Recipe preview endpoint returns full RecipeContentModel — used for both preview page and to populate edit form
- Create/Update endpoints expect RecipeContentModel as nested JSON — form must serialize content correctly
- Deactivate is a soft delete (sets IsActive = false) — recipe remains in DB but filtered from public list

</code_context>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches. Key constraint: all recipe content (stages, fields, rules, roles) is stored as JSONB in a single RecipeContentModel, so the form must handle this nested structure.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 10-recipe-management-ui*
*Context gathered: 2026-04-01*
