# Phase 10: Recipe Management UI - Research

**Researched:** 2026-04-01
**Domain:** Blazor Server admin UI for CRUD operations on industry recipes with complex nested content
**Confidence:** HIGH

## Summary

Phase 10 builds admin UI pages for full recipe lifecycle management within the authenticated admin shell established in Phase 9. The backend Recipe API is mature and fully tested; this phase focuses on translating those endpoints into Blazor Server pages with forms, tables, and navigation.

The critical challenge is serializing/deserializing complex nested JSON (`RecipeContentModel` with PipelineStages, CustomFields, WorkflowRules, Roles, SampleLeads) from Blazor EditForm to the API. This requires careful handling of list mutations (add/remove/reorder) in the form before POST/PUT. Standard Blazor EditForm patterns apply—client-side validation mirrors backend FluentValidation rules.

**Primary recommendation:** Use Blazor EditForm with two-way binding on a locally-held RecipeContentModel, implement custom handlers for list mutations (stages/fields/rules/roles), validate client-side, serialize to JSON on submit, and POST/PUT the entire structure atomically. Follow Phase 9 Login.razor patterns for error banners and loading states.

## User Constraints (from CONTEXT.md)

### Locked Decisions

**Recipe List Page:**
- Standard data table with columns: Name, Industry (slug), Status, Version, Stage/Field/Rule/Role counts, Actions
- Status displayed as colored badge — green for active, gray/muted for inactive
- Show both active and deactivated recipes with a status filter toggle
- Empty state: centered message with "Create your first recipe" CTA button
- Actions column: Edit, Preview, Deactivate buttons per row

**Recipe Form (Create/Edit):**
- Multi-section form on dedicated page — metadata at top (name, description, industry slug, icon identifier, isBlank toggle), content sections below
- Inline validation matching Phase 9 pattern — errors below each field + summary banner at top
- Industry slug auto-generated from name with manual override option
- Single "Save" button at bottom — saves entire recipe atomically via POST (create) or PUT (edit)
- Create page at `/admin/recipes/create`, Edit page at `/admin/recipes/{id}/edit`

**Recipe Content Editor (within Create/Edit form):**
- Pipeline stages: inline editable list with add/remove/reorder — fields: Name, Order, StageType (Entry/Active/ClosedWon/ClosedLost)
- Custom fields: inline editable list with add/remove — fields: Name, Type, IsRequired toggle, Options (for select types)
- Workflow rules: inline editable list with add/remove — fields: TriggerType, Conditions (JSON), ActionType, ActionConfig
- Roles: simple list with add/remove — fields: Name, Description
- Each content section is a collapsible card within the form

**Recipe Preview:**
- Full page at `/admin/recipes/{id}/preview` — more space for complex nested content
- Read-only tabbed view with tabs: Stages, Fields, Rules, Roles, Sample Leads
- Each tab shows clean tables matching the RecipeContentModel structure
- "Back to List" and "Edit" navigation buttons at top

### Claude's Discretion

- Exact Tailwind styling for table rows, badges, form inputs (maintain consistency with Phase 9 palette)
- Loading skeleton/spinner approach while API calls complete
- Pagination vs. scroll for the recipe list (likely few enough to show all)
- Toast/notification style for success messages after save/deactivate
- Exact layout proportions for the content editor sections

### Deferred Ideas (OUT OF SCOPE)

None.

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| RCUI-01 | Admin can view a list of all industry recipes with name, industry, and status | RecipeListEndpoint returns Id, Name, Description, IconIdentifier, IndustrySlug, IsBlank, Version, StageCount, FieldCount, RuleCount, RoleCount; Blazor DataTable component with Tailwind styling; status badge filtering |
| RCUI-02 | Admin can create a new recipe with name, industry, and content (stages, fields, rules, roles) | POST /api/recipes endpoint with FluentValidation; Blazor EditForm with nested RecipeContentModel; client-side validation matching backend rules (Name, IndustrySlug uniqueness, Content structure) |
| RCUI-03 | Admin can edit an existing recipe's details and content | PUT /api/recipes/{id} endpoint; RecipePreviewEndpoint provides full content for form population; EditForm with two-way binding on all nested structures |
| RCUI-04 | Admin can preview a recipe's full configuration before applying | RecipePreviewEndpoint returns full RecipeContentModel; tabbed read-only view (Stages, Fields, Rules, Roles, SampleLeads); no EditForm, display-only components |
| RCUI-05 | Admin can deactivate a recipe (soft disable, not delete) | DELETE /api/recipes/{id} endpoint returns 200 OK with deactivation message; async action in table, updates list without reload (soft UX) or refreshes list |

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Blazor Server | .NET 10.0 | Component-based UI framework | Integrated with .NET Aspire, established in Phase 9 |
| EditForm | Blazor built-in | Form binding and validation | Standard Blazor pattern for data entry; used in Login.razor (Phase 9) |
| DataAnnotationsValidator | Blazor built-in | Client-side validation | Mirrors backend FluentValidation for user feedback; matches Phase 9 Login pattern |
| IHttpClientFactory | .NET built-in | Named HttpClient "AdminApi" with BearerTokenHandler | Established in Phase 9; auto-injects JWT token on all API calls |
| System.Text.Json | .NET built-in | Serialize/deserialize RecipeContentModel | Matches API contract; no additional dependencies |
| Tailwind CSS 4 (standalone CLI) | 4.x | Utility-first styling | Established in Phase 9 via MSBuild integration; no Node.js required |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Heroicons (SVG) | Inline | Icons for table actions, badges, form sections | Established pattern in Phase 9 (AdminSidebar.razor, Login.razor) |
| InputSelect / InputNumber / InputCheckbox | Blazor built-in | Form field types for stage type, field type, required toggle | Standard Blazor input components; no extra dependencies |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Blazor EditForm | MudBlazor or Syncfusion | Would violate PROJECT.md decision "no component libraries"; EditForm is sufficient for admin UI |
| IHttpClientFactory | Direct HttpClient inject | IHttpClientFactory allows named clients and DelegatingHandlers; better testability |
| System.Text.Json | Newtonsoft.Json | System.Text.Json is built-in, matches ApiService, simpler dependency story |

**Installation:**
```bash
# No additional packages required — all are already in IronMonkey.Web.csproj
# Verify existing:
dotnet package search EditForm  # Built-in to Blazor
dotnet package search DataAnnotationsValidator  # Built-in to Blazor
```

**Version verification:**
- Blazor Server: .NET 10.0 (confirmed in CLAUDE.md; csproj targets net10.0)
- Tailwind CSS v4 standalone CLI: verified working in Phase 9
- IHttpClientFactory + BearerTokenHandler: established in Phase 9 (IronMonkey.Web/HttpHandlers/BearerTokenHandler.cs)

## Architecture Patterns

### Recommended Project Structure
```
IronMonkey.Web/Components/Pages/Admin/
├── Recipes/
│   ├── RecipeList.razor                    # List view, filter, actions
│   ├── RecipeCreate.razor                  # Create form, new RecipeContentModel()
│   ├── RecipeEdit.razor                    # Edit form, populate from preview endpoint
│   ├── RecipePreview.razor                 # Read-only tabbed view
│   └── Shared/
│       ├── RecipeContentEditor.razor       # Reusable form sections (stages, fields, rules, roles)
│       ├── StageListEditor.razor           # Inline list for stages with add/remove/reorder
│       ├── FieldListEditor.razor           # Inline list for custom fields
│       ├── RuleListEditor.razor            # Inline list for workflow rules
│       ├── RoleListEditor.razor            # Inline list for roles
│       └── PreviewTabContent.razor         # Read-only tabs (Stages, Fields, Rules, Roles, SampleLeads)
```

### Pattern 1: RecipeList Page — Table with Filter, Actions, Empty State

**What:** Displays all recipes (active + deactivated) in a data table with filtering by status. Each row shows computed counts (stages, fields, rules, roles) from the API. Actions column has Edit, Preview, Deactivate buttons.

**When to use:** Always — main entry point for recipe management.

**Example:**
```razor
@* Source: Phase 9 Login.razor validation pattern adapted to table *@
@page "/admin/recipes"
@attribute [Authorize]
@inject IHttpClientFactory HttpClientFactory
@inject NavigationManager Nav

<PageTitle>Recipe Management — IronMonkey Admin</PageTitle>

<div class="space-y-6">
    <!-- Heading + Create button -->
    <div class="flex items-center justify-between">
        <h1 class="text-2xl font-bold text-slate-900">Recipe Management</h1>
        <button @onclick="() => Nav.NavigateTo('/admin/recipes/create')"
                class="inline-flex items-center gap-2 rounded-lg bg-indigo-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500">
            <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-4 h-4">
                <path stroke-linecap="round" stroke-linejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
            </svg>
            Create Recipe
        </button>
    </div>

    <!-- Status filter toggle -->
    <div class="flex items-center gap-2">
        <label class="flex items-center gap-2 text-sm font-medium text-slate-700">
            <input type="checkbox" @bind="_showInactive" class="rounded border-slate-300" />
            Show Deactivated Recipes
        </label>
    </div>

    <!-- Error banner (matches Login.razor D-02) -->
    @if (!string.IsNullOrEmpty(_errorBanner))
    {
        <div class="rounded-lg bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
            @_errorBanner
        </div>
    }

    <!-- Success banner -->
    @if (!string.IsNullOrEmpty(_successMessage))
    {
        <div class="rounded-lg bg-green-50 border border-green-200 px-4 py-3 text-sm text-green-700">
            @_successMessage
        </div>
    }

    <!-- Loading state -->
    @if (_isLoading)
    {
        <div class="text-center py-12">
            <p class="text-sm text-slate-500">Loading recipes...</p>
        </div>
    }
    else if (_recipes.Count == 0)
    {
        <!-- Empty state -->
        <div class="rounded-xl border border-dashed border-slate-300 bg-slate-50 py-16 text-center">
            <p class="text-slate-600">No recipes yet. Create one to get started.</p>
            <button @onclick="() => Nav.NavigateTo('/admin/recipes/create')"
                    class="mt-4 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-500">
                Create your first recipe
            </button>
        </div>
    }
    else
    {
        <!-- Table -->
        <div class="overflow-x-auto rounded-lg border border-slate-200">
            <table class="w-full">
                <thead class="bg-slate-50 border-b border-slate-200">
                    <tr>
                        <th class="px-6 py-3 text-left text-xs font-semibold text-slate-900 uppercase tracking-wide">Name</th>
                        <th class="px-6 py-3 text-left text-xs font-semibold text-slate-900 uppercase tracking-wide">Industry</th>
                        <th class="px-6 py-3 text-left text-xs font-semibold text-slate-900 uppercase tracking-wide">Status</th>
                        <th class="px-6 py-3 text-center text-xs font-semibold text-slate-900 uppercase tracking-wide">Stages / Fields / Rules / Roles</th>
                        <th class="px-6 py-3 text-left text-xs font-semibold text-slate-900 uppercase tracking-wide">Version</th>
                        <th class="px-6 py-3 text-right text-xs font-semibold text-slate-900 uppercase tracking-wide">Actions</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var recipe in DisplayedRecipes)
                    {
                        <tr class="border-b border-slate-100 hover:bg-slate-50">
                            <td class="px-6 py-4 text-sm text-slate-900">@recipe.Name</td>
                            <td class="px-6 py-4 text-sm text-slate-600">@recipe.IndustrySlug</td>
                            <td class="px-6 py-4 text-sm">
                                <span class="inline-flex items-center gap-1 rounded-full px-2 py-1 text-xs font-semibold @GetStatusBadgeClass(recipe.IsActive)">
                                    @(recipe.IsActive ? "Active" : "Inactive")
                                </span>
                            </td>
                            <td class="px-6 py-4 text-center text-sm text-slate-500">@recipe.StageCount / @recipe.FieldCount / @recipe.RuleCount / @recipe.RoleCount</td>
                            <td class="px-6 py-4 text-sm text-slate-600">v@recipe.Version</td>
                            <td class="px-6 py-4 text-right space-x-2">
                                <button @onclick="() => Nav.NavigateTo($'/admin/recipes/{recipe.Id}/preview')"
                                        class="inline-text text-sm font-medium text-indigo-600 hover:text-indigo-700">Preview</button>
                                <button @onclick="() => Nav.NavigateTo($'/admin/recipes/{recipe.Id}/edit')"
                                        class="inline-text text-sm font-medium text-indigo-600 hover:text-indigo-700">Edit</button>
                                @if (recipe.IsActive)
                                {
                                    <button @onclick="() => HandleDeactivateAsync(recipe.Id)"
                                            disabled="@_isDeactivating"
                                            class="inline-text text-sm font-medium text-red-600 hover:text-red-700 disabled:opacity-50">
                                        @(_isDeactivating && _deactivatingId == recipe.Id ? "..." : "Deactivate")
                                    </button>
                                }
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
    }
</div>

@code {
    private List<RecipeListDto> _recipes = [];
    private bool _isLoading = true;
    private bool _showInactive = false;
    private string? _errorBanner = null;
    private string? _successMessage = null;
    private bool _isDeactivating = false;
    private Guid? _deactivatingId = null;

    private List<RecipeListDto> DisplayedRecipes =>
        _showInactive ? _recipes : _recipes.Where(r => r.IsActive).ToList();

    protected override async Task OnInitializedAsync()
    {
        await LoadRecipesAsync();
    }

    private async Task LoadRecipesAsync()
    {
        _isLoading = true;
        _errorBanner = null;

        try
        {
            var client = HttpClientFactory.CreateClient("AdminApi");
            var recipes = await client.GetFromJsonAsync<List<RecipeListDto>>("/api/recipes");
            _recipes = recipes ?? [];
        }
        catch (Exception ex)
        {
            _errorBanner = $"Failed to load recipes: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task HandleDeactivateAsync(Guid id)
    {
        _isDeactivating = true;
        _deactivatingId = id;
        _errorBanner = null;
        _successMessage = null;

        try
        {
            var client = HttpClientFactory.CreateClient("AdminApi");
            var response = await client.DeleteAsync($"/api/recipes/{id}");
            if (response.IsSuccessStatusCode)
            {
                _recipes.FirstOrDefault(r => r.Id == id)!.IsActive = false;
                _successMessage = "Recipe deactivated successfully.";
            }
            else
            {
                _errorBanner = "Failed to deactivate recipe.";
            }
        }
        catch (Exception ex)
        {
            _errorBanner = $"Error: {ex.Message}";
        }
        finally
        {
            _isDeactivating = false;
            _deactivatingId = null;
        }
    }

    private string GetStatusBadgeClass(bool isActive) =>
        isActive ? "bg-green-100 text-green-800" : "bg-slate-100 text-slate-600";

    private record RecipeListDto(Guid Id, string Name, string Description, string IconIdentifier, string IndustrySlug, bool IsBlank, int Version, int StageCount, int FieldCount, int RuleCount, int RoleCount)
    {
        public bool IsActive { get; set; } = true;
    };
}
```

### Pattern 2: RecipeCreate & RecipeEdit Pages — Form with Nested Content Sections

**What:** Multi-section form with metadata (Name, Description, IndustrySlug, IconIdentifier, IsBlank) at the top and collapsible content sections (Stages, Fields, Rules, Roles). Uses EditForm with two-way binding on RecipeContentModel. On create, uses POST; on edit, fetches full recipe from preview endpoint and uses PUT.

**When to use:** Always for both create and edit routes.

**Key challenge:** Blazor EditForm doesn't directly support list mutations (add/remove/reorder within form submission). Solution: maintain mutable list references in code-behind, provide Add/Remove/Reorder button handlers, re-render collections, then submit entire structure as JSON.

**Example:**
```csharp
// RecipeEditViewModel — matches backend CreateRecipeEndpoint.Request
public class RecipeEditViewModel
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IconIdentifier { get; set; } = string.Empty;
    public string IndustrySlug { get; set; } = string.Empty;
    public bool IsBlank { get; set; } = false;
    public RecipeContentModel Content { get; set; } = new();
}

// In RecipeCreate.razor @code block:
private RecipeEditViewModel _model = new() 
{ 
    Content = new RecipeContentModel()
};

private async Task HandleAddStageAsync()
{
    _model.Content.PipelineStages.Add(new PipelineStageDefinition 
    { 
        Name = "New Stage", 
        Order = _model.Content.PipelineStages.Count,
        StageType = "Active"
    });
    // Force re-render of the list
    StateHasChanged();
}

private async Task HandleSubmitAsync()
{
    var client = HttpClientFactory.CreateClient("AdminApi");
    var response = await client.PostAsJsonAsync("/api/recipes", _model);
    // Handle response
}
```

### Pattern 3: RecipePreview Page — Read-Only Tabbed View

**What:** Full-page view with tabs for Stages, Fields, Rules, Roles, SampleLeads. No EditForm — display only. Tables show each nested collection. Fetches data from GET /api/recipes/{id} preview endpoint once on load.

**When to use:** `/admin/recipes/{id}/preview` route.

**Example:**
```razor
@* Source: Follows standard Blazor tabbed component pattern *@
@page "/admin/recipes/{Id:guid}/preview"
@attribute [Authorize]
@inject IHttpClientFactory HttpClientFactory
@inject NavigationManager Nav

<div class="mb-6 flex items-center justify-between">
    <div>
        <button @onclick="() => Nav.NavigateTo('/admin/recipes')" 
                class="mb-2 text-sm text-indigo-600 hover:text-indigo-700">← Back to List</button>
        <h1 class="text-2xl font-bold text-slate-900">@_recipe?.Name</h1>
    </div>
    <button @onclick="() => Nav.NavigateTo($'/admin/recipes/{Id}/edit')"
            class="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-500">
        Edit Recipe
    </button>
</div>

@if (_isLoading)
{
    <p>Loading preview...</p>
}
else if (_recipe == null)
{
    <p class="text-red-600">Recipe not found.</p>
}
else
{
    <!-- Tabbed interface -->
    <div class="border-b border-slate-200">
        <nav class="flex space-x-8">
            @foreach (var tab in new[] { "Stages", "Fields", "Rules", "Roles", "Sample Leads" })
            {
                <button @onclick="() => _activeTab = tab"
                        class="px-1 py-4 text-sm font-medium @(_activeTab == tab ? "border-b-2 border-indigo-600 text-indigo-600" : "text-slate-600 hover:text-slate-900")">
                    @tab
                </button>
            }
        </nav>
    </div>

    <!-- Tab content -->
    <div class="mt-6">
        @switch (_activeTab)
        {
            case "Stages":
                <PreviewTabContent Title="Pipeline Stages" Items="_recipe.Content.PipelineStages.Cast<object>().ToList()">
                    <!-- Render stages table -->
                </PreviewTabContent>
                break;
            // ... other tabs
        }
    </div>
}

@code {
    [Parameter] public Guid Id { get; set; }

    private RecipePreviewDto? _recipe = null;
    private bool _isLoading = true;
    private string _activeTab = "Stages";

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("AdminApi");
        try
        {
            _recipe = await client.GetFromJsonAsync<RecipePreviewDto>($"/api/recipes/{Id}");
        }
        catch
        {
            // Handle error
        }
        finally
        {
            _isLoading = false;
        }
    }

    private record RecipePreviewDto(Guid Id, string Name, string Description, RecipeContentModel Content);
}
```

### Anti-Patterns to Avoid

- **Don't serialize RecipeContentModel inline in form markup.** Keep one mutable instance in code-behind (`_model.Content`), mutate it via Add/Remove handlers, and serialize on submit via `PostAsJsonAsync`.
- **Don't use nested EditForm components.** EditForm doesn't support nesting; use a single top-level EditForm with manual list mutation handlers for content sections.
- **Don't implement client-side IndustrySlug uniqueness validation.** This must be checked server-side via the CreateRecipeEndpoint validator; client can only check format (lowercase, hyphens). Let the API rejection trigger the error banner.
- **Don't fetch the full recipe on every keystroke.** Load RecipePreviewEndpoint data once in OnInitializedAsync for edit forms; maintain local state for mutations.
- **Don't render hundreds of recipes without pagination.** Assume recipes list is small (<100); use pagination if tests show otherwise.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Form validation with inline errors | Custom validation logic | Blazor DataAnnotationsValidator + EditForm | Built-in, matches Phase 9 pattern, handles async validation |
| JSON serialization round-trip for RecipeContentModel | Custom serialization code | System.Text.Json GetFromJsonAsync/PostAsJsonAsync | Handles null coalescing, matches API contract, tested in backend |
| List reordering (drag-drop for stages) | Custom drag-drop handler | HTML5 drag-drop events + JavaScript interop (if needed, but defer to "Claude's Discretion") | Simple add/remove sufficient for v1; reorder can be added later via hidden OrderField |
| Tabbed interface | Custom tab logic | Simple @switch on _activeTab string with button click handlers | Minimal, no extra dependencies, Tailwind styling |
| Status badge styling | Custom badge classes | Tailwind utility classes (bg-green-100 text-green-800, etc.) | Matches Phase 9 palette, reusable |
| Table with sorting/filtering | Custom table logic | Server-side filtering via query params or client-side LINQ on loaded list | Small recipe set suggests client-side sufficient; sorting deferred to later phase |

**Key insight:** Blazor Server gives us EditForm, validation, and HTTP client for free. The only custom work is form state management (list mutations) and navigation between pages. Everything else layers on top of existing Blazor patterns.

## Common Pitfalls

### Pitfall 1: Forgetting to Validate IndustrySlug Uniqueness Client-Side

**What goes wrong:** Admin creates two recipes with the same industry slug. Backend rejects with 400 ValidationError. Admin sees error but doesn't understand why — client-side form should have warned them.

**Why it happens:** IndustrySlug uniqueness is a cross-record constraint (only the API knows all recipe slugs). Client can't check this without fetching the full list first.

**How to avoid:** 
1. Load the full recipe list once when page initializes (already done for RecipeList).
2. On RecipeCreate, validate IndustrySlug against loaded list before submit.
3. On RecipeEdit, exclude current recipe from uniqueness check.
4. If validation fails client-side, show inline error. If API rejects with ValidationError, parse response and display in error banner.

**Warning signs:**
- IndustrySlug field never shows validation error even when backend rejects it.
- Admin is confused about why create failed.

### Pitfall 2: RecipeContentModel Serialization Losing Data on Round-Trip

**What goes wrong:** Admin creates a recipe with 3 stages, saves it. When they edit the same recipe, only 1 stage appears. The other 2 were lost.

**Why it happens:** JSON deserialization in System.Text.Json requires default constructors and public properties. If RecipeContentModel or nested types (PipelineStageDefinition, etc.) are missing default constructors or use init-only properties, deserialization fails silently, returning empty/partial objects.

**How to avoid:**
1. Verify RecipeContentModel and all nested types (PipelineStageDefinition, CustomFieldDefinitionDto, etc.) have default parameterless constructors and public settable properties.
2. Test round-trip serialization locally before shipping: `var json = JsonSerializer.Serialize(model); var deserialized = JsonSerializer.Deserialize<RecipeContentModel>(json); Assert.Equal(model, deserialized)`.
3. Add this test to Phase 10 test suite (integration test that creates a recipe, then edits it, verifying all fields persist).

**Warning signs:**
- After editing a recipe, count of stages/fields/rules/roles suddenly drops.
- Workflow rules appear as empty `{}` in the form.

### Pitfall 3: EditForm Binding Two-Way with List Mutations Not Triggering StateHasChanged

**What goes wrong:** Admin clicks "Add Stage" button, new stage appears briefly in the UI, but then disappears. The button handler added the stage to the list, but the form didn't re-render because EditForm only tracks property mutations, not list mutations.

**Why it happens:** Blazor's two-way binding (`@bind-Value`) watches for assignment changes (e.g., `_model.Name = "new name"`), not for mutations on existing objects (e.g., `_model.Content.PipelineStages.Add(...)`). EditForm re-renders when you update the model, but not when you mutate its properties.

**How to avoid:**
1. After any list mutation (Add, Remove, Reorder), call `StateHasChanged()` to force re-render.
2. For add: `_model.Content.PipelineStages.Add(...); StateHasChanged();`
3. For remove: `_model.Content.PipelineStages.RemoveAt(index); StateHasChanged();`
4. Test this behavior in the form page itself (manual testing; integration tests via Testcontainers are complex for UI state).

**Warning signs:**
- "Add Stage" button fires, then nothing happens or the added stage flashes and disappears.
- The JSON sent to API is missing the added stages even though they appeared in the UI.

### Pitfall 4: BearerTokenHandler Silently Failing on Expired Token

**What goes wrong:** Admin is editing a recipe for 30 minutes. Session expires. They click Save. The API returns 401 Unauthorized. BearerTokenHandler logs a warning but doesn't redirect to login. Admin sees no UI feedback.

**Why it happens:** BearerTokenHandler detects 401 but doesn't have access to NavigationManager to redirect. The 401 propagates up the call stack in the Razor component, but if error handling is incomplete, admin sees nothing.

**How to avoid:**
1. In every API call handler, catch HttpRequestException or check response.StatusCode.
2. If 401 Unauthorized, call `Nav.NavigateTo("/login")` explicitly.
3. Or add global error handling in a layout component that listens for 401s (deferred to future polish phase).
4. For Phase 10: catch 401 in each API call handler and redirect.

**Example:**
```csharp
try
{
    var response = await client.PostAsJsonAsync("/api/recipes", _model);
    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
    {
        Nav.NavigateTo("/login");
        return;
    }
    // ... handle other response codes
}
catch (Exception ex)
{
    _errorBanner = $"Error: {ex.Message}";
}
```

**Warning signs:**
- After session expires, admin clicks a button and nothing happens (no redirect, no error message).
- API returns 401 but admin remains on the page as if nothing happened.

### Pitfall 5: RecipeContentModel Nested Collections Initialized Incorrectly

**What goes wrong:** Admin opens the RecipeCreate form. They try to add a field. NullReferenceException because CustomFields is null instead of an empty list.

**Why it happens:** RecipeContentModel initializes collections with `[]` (C# 12 collection initializer), but if any deserialization path bypasses this (e.g., `new RecipeContentModel()`), collections may be null.

**How to avoid:**
1. Verify RecipeContentModel and all nested collection properties are initialized in the constructor or property declaration: `public List<CustomFieldDefinitionDto> CustomFields { get; set; } = [];`.
2. On form initialization, ensure `_model.Content ??= new RecipeContentModel();` to handle null.
3. Before any mutation: `_model.Content.CustomFields ??= [];`.

**Warning signs:**
- NullReferenceException when trying to add a field/stage/rule.
- Form loads but content sections appear empty or broken.

## Code Examples

Verified patterns from existing code:

### API Error Handling (Login.razor Pattern, Applied to Recipe Operations)

```csharp
// Source: Login.razor, lines 90-136
// Adapted for recipe operations

private async Task HandleSaveRecipeAsync()
{
    _isLoading = true;
    _errorBanner = null;

    try
    {
        var client = HttpClientFactory.CreateClient("AdminApi");
        var response = await client.PostAsJsonAsync("/api/recipes", _model);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<RecipeCreateResponse>();
            _successMessage = "Recipe created successfully.";
            Nav.NavigateTo($"/admin/recipes/{result?.RecipeId}/edit");
        }
        else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // Parse ValidationError response from API
            var error = await response.Content.ReadAsStringAsync();
            _errorBanner = $"Validation error: {error}";
        }
        else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            Nav.NavigateTo("/login");
        }
        else
        {
            _errorBanner = "An error occurred. Please try again.";
        }
    }
    catch (Exception ex)
    {
        _errorBanner = $"Unable to save recipe: {ex.Message}";
    }
    finally
    {
        _isLoading = false;
    }
}

private record RecipeCreateResponse(Guid RecipeId, string Message);
```

### EditForm with Nested List Mutations

```razor
@* Source: Blazor EditForm pattern, adapted for nested RecipeContentModel *@

<EditForm Model="_model" OnValidSubmit="HandleSaveRecipeAsync">
    <DataAnnotationsValidator />

    <!-- Metadata section -->
    <div class="mb-6 p-4 border border-slate-200 rounded-lg">
        <h3 class="font-semibold text-slate-900 mb-4">Recipe Details</h3>

        <div class="mb-4">
            <label class="block text-sm font-medium text-slate-700 mb-1">Name</label>
            <InputText @bind-Value="_model.Name" 
                       class="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm" />
            <ValidationMessage For="() => _model.Name" class="mt-1 text-xs text-red-600" />
        </div>

        <div class="mb-4">
            <label class="block text-sm font-medium text-slate-700 mb-1">Description</label>
            <InputTextArea @bind-Value="_model.Description" 
                           rows="3"
                           class="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm" />
            <ValidationMessage For="() => _model.Description" class="mt-1 text-xs text-red-600" />
        </div>

        <div class="mb-4">
            <label class="block text-sm font-medium text-slate-700 mb-1">Industry Slug</label>
            <InputText @bind-Value="_model.IndustrySlug" 
                       placeholder="e.g., automobile-dealership"
                       class="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm" />
            <ValidationMessage For="() => _model.IndustrySlug" class="mt-1 text-xs text-red-600" />
        </div>
    </div>

    <!-- Stages section -->
    <div class="mb-6 p-4 border border-slate-200 rounded-lg">
        <h3 class="font-semibold text-slate-900 mb-4">Pipeline Stages</h3>
        
        <div class="space-y-2 mb-4">
            @foreach (var (stage, idx) in _model.Content.PipelineStages.Select((s, i) => (s, i)))
            {
                <div class="flex gap-2 items-end">
                    <div class="flex-1">
                        <label class="text-xs text-slate-600">Name</label>
                        <InputText @bind-Value="stage.Name" class="w-full rounded border border-slate-300 px-2 py-1 text-sm" />
                    </div>
                    <div class="w-20">
                        <label class="text-xs text-slate-600">Order</label>
                        <InputNumber @bind-Value="stage.Order" class="w-full rounded border border-slate-300 px-2 py-1 text-sm" />
                    </div>
                    <div class="flex-1">
                        <label class="text-xs text-slate-600">Type</label>
                        <InputSelect @bind-Value="stage.StageType" class="w-full rounded border border-slate-300 px-2 py-1 text-sm">
                            <option value="Entry">Entry</option>
                            <option value="Active">Active</option>
                            <option value="ClosedWon">Closed Won</option>
                            <option value="ClosedLost">Closed Lost</option>
                        </InputSelect>
                    </div>
                    <button type="button" @onclick="() => HandleRemoveStageAsync(idx)"
                            class="rounded bg-red-100 px-3 py-1 text-sm text-red-700 hover:bg-red-200">
                        Remove
                    </button>
                </div>
            }
        </div>
        
        <button type="button" @onclick="HandleAddStageAsync"
                class="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-200">
            + Add Stage
        </button>
    </div>

    <!-- Submit -->
    <div class="flex gap-2">
        <button type="submit" disabled="@_isLoading"
                class="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-500 disabled:opacity-60">
            @(_isLoading ? "Saving..." : "Save Recipe")
        </button>
        <button type="button" @onclick="() => Nav.NavigateTo('/admin/recipes')"
                class="rounded-lg bg-slate-200 px-4 py-2 text-sm font-semibold text-slate-700 hover:bg-slate-300">
            Cancel
        </button>
    </div>
</EditForm>

@code {
    private RecipeEditViewModel _model = new();
    private bool _isLoading = false;

    private async Task HandleAddStageAsync()
    {
        _model.Content.PipelineStages.Add(new PipelineStageDefinition 
        { 
            Name = "New Stage",
            Order = _model.Content.PipelineStages.Count,
            StageType = "Active"
        });
        StateHasChanged();
    }

    private async Task HandleRemoveStageAsync(int idx)
    {
        _model.Content.PipelineStages.RemoveAt(idx);
        StateHasChanged();
    }

    private async Task HandleSaveRecipeAsync()
    {
        _isLoading = true;
        try
        {
            var client = HttpClientFactory.CreateClient("AdminApi");
            var response = await client.PostAsJsonAsync("/api/recipes", _model);
            // ... handle response
        }
        finally
        {
            _isLoading = false;
        }
    }

    private class RecipeEditViewModel
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconIdentifier { get; set; } = string.Empty;
        public string IndustrySlug { get; set; } = string.Empty;
        public bool IsBlank { get; set; } = false;
        public RecipeContentModel Content { get; set; } = new();
    }
}
```

### Data Annotation Validators (Matching Backend FluentValidation)

```csharp
// Source: Matches CreateRecipeEndpoint validator rules
// Applied in Blazor form model

public class RecipeCreateViewModel
{
    [Required(ErrorMessage = "Name is required")]
    [StringLength(200, ErrorMessage = "Name must be 200 characters or less")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Description is required")]
    [StringLength(2000, ErrorMessage = "Description must be 2000 characters or less")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Icon identifier is required")]
    [StringLength(100, ErrorMessage = "Icon must be 100 characters or less")]
    public string IconIdentifier { get; set; } = string.Empty;

    [Required(ErrorMessage = "Industry slug is required")]
    [StringLength(100, ErrorMessage = "Slug must be 100 characters or less")]
    [RegularExpression(@"^[a-z0-9\-]+$", ErrorMessage = "Slug must contain only lowercase letters, numbers, and hyphens")]
    public string IndustrySlug { get; set; } = string.Empty;

    public bool IsBlank { get; set; } = false;

    [ValidateComplexType]
    public RecipeContentModel Content { get; set; } = new();
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Forms built in plain HTML with jQuery validation | Blazor EditForm + DataAnnotationsValidator | Phase 9 (UI Foundation) | Simplified client-side validation, matches backend constraints, zero custom JS |
| Custom HTTP client setup per page | IHttpClientFactory with named "AdminApi" client + BearerTokenHandler | Phase 9 | Centralized token injection, consistent auth across all admin pages |
| Newtonsoft.Json for serialization | System.Text.Json | Phase 9+ | Lighter weight, built-in, matches API contract, simpler AOT story |
| Manual form state management | Blazor EditForm with two-way binding + DataAnnotationsValidator | Phase 9+ | Reduces boilerplate, validation errors display automatically |

**Deprecated/outdated:**
- Manual JWT token management — replaced with ProtectedSessionStorage + AdminAuthenticationStateProvider (Phase 9)
- MudBlazor components — explicitly deferred per PROJECT.md; native Razor + Tailwind only

## Environment Availability

No external dependencies beyond .NET 10.0 and existing IronMonkey stack. All required libraries are already in IronMonkey.Web.csproj:
- `Blazor Server` (built-in with ASP.NET Core)
- `System.Text.Json` (built-in)
- `IHttpClientFactory` (built-in)
- `EditForm`, `DataAnnotationsValidator`, input components (built-in to Blazor)
- `Tailwind CSS` standalone CLI (Phase 9, verified working)

**Verification:**
```bash
dotnet build IronMonkey.Web
# Should succeed if all dependencies are installed
```

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 (backend integration tests); no Blazor-specific UI tests planned |
| Config file | IronMonkey.Tests/IronMonkey.Tests.csproj |
| Quick run command | `dotnet test IronMonkey.Tests --filter "RecipeEndpoint" -x` |
| Full suite command | `dotnet test IronMonkey.Tests -x` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| RCUI-01 | List page displays all active recipes with counts | Manual (Blazor UI) | N/A — no automated UI testing planned for v1.2 | N/A |
| RCUI-02 | Create form POSTs valid recipe, receives 201, displays success | Integration | `dotnet test IronMonkey.Tests --filter "RecipeEndpointTests" -x` | ✅ Phase 9 created RecipeEndpointTests.cs |
| RCUI-03 | Edit form fetches recipe, PUTs updated content, receives 200 | Integration | `dotnet test IronMonkey.Tests --filter "RecipeEndpointTests" -x` | ✅ RecipeEndpointTests covers create/update/deactivate |
| RCUI-04 | Preview page fetches recipe, displays tabs, deactivate button works | Manual (Blazor UI) | N/A | N/A |
| RCUI-05 | Deactivate button DELETEs recipe, updates list status | Integration | `dotnet test IronMonkey.Tests --filter "RecipeEndpointTests" -x` | ✅ RecipeEndpointTests covers deactivate endpoint |

### Sampling Rate

- **Per task commit:** `dotnet test IronMonkey.Tests --filter "RecipeEndpointTests" -x` (backend API integrity)
- **Per wave merge:** `dotnet test IronMonkey.Tests -x` (full suite)
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps

- [ ] Integration test for recipe serialization round-trip (create, then edit, verify all fields persist) — covers Pitfall #2
- [ ] Integration test for IndustrySlug uniqueness validation — backend already validates; frontend can defer detailed testing
- [ ] Blazor page smoke tests (if infrastructure available) — currently no Blazor-specific test framework in place; defer to future phase

Note: Phase 10 focuses on Blazor page implementation. Backend Recipe API tests (RecipeEndpointTests.cs) already exist and cover RCUI-02, RCUI-03, RCUI-05 at the API level. Phase 10 manual testing validates the UI wiring.

## Open Questions

1. **Should recipe list show both active and deactivated recipes by default, or only active?**
   - CONTEXT.md says: "Show both active and deactivated recipes with a status filter toggle"
   - Recommendation: Default to showing only active (cleaner initial view), toggle to show deactivated (less common workflow)

2. **Should "reorder stages" use HTML5 drag-drop or just manual order field?**
   - CONTEXT.md is silent (delegated to "Claude's Discretion")
   - Recommendation: Start simple with manual `Order` field (InputNumber). If UX testing shows demand, add drag-drop in Phase 11.

3. **Should the Industry Slug auto-generate from Name in real-time or only on manual request?**
   - CONTEXT.md says: "auto-generated from name with manual override option"
   - Recommendation: Auto-generate on blur (when Name field loses focus), allow manual edit, warn if slug conflicts with existing.

4. **What format for Conditions/ActionJson in workflow rules?**
   - Backend RecipeContentModel stores these as strings: `ConditionJson`, `ActionJson`
   - CONTEXT.md doesn't specify the JSON structure
   - Recommendation: For Phase 10, treat as opaque JSON text fields (TextArea). Full rule builder can be Phase 13+.

## Sources

### Primary (HIGH confidence)
- **CONTEXT.md** (Phase 10 decision document) — Decisions D-01 through D-18, canonical references to API endpoints and existing code patterns
- **REQUIREMENTS.md** (Project requirements) — RCUI-01 through RCUI-05 definitions
- **IronMonkey.Web/Components/Pages/Login.razor** — Form validation pattern, error handling, loading states (Phase 9 template)
- **IronMonkey.Web/Components/Layout/AdminSidebar.razor** — Navigation structure, Tailwind styling, Heroicons usage (Phase 9 template)
- **IronMonkey.ApiService/Features/Recipes/*.cs** — Five endpoints (List, Preview, Create, Update, Deactivate) with FluentValidation, request/response shapes, JSON serialization
- **IronMonkey.Data/RecipeContent/RecipeContentModel.cs** — Data model structure and nested type definitions
- **IronMonkey.Tests/Integration/RecipeEndpointTests.cs** — Verified endpoint behavior, test patterns
- **IronMonkey.Web/HttpHandlers/BearerTokenHandler.cs** — JWT token injection pattern
- **CLAUDE.md** — Project conventions, build commands, architecture decisions

### Secondary (MEDIUM confidence)
- **Blazor documentation (built-in)** — EditForm, DataAnnotationsValidator, InputText, InputSelect, two-way binding patterns
- **.NET 10.0 System.Text.Json** — Serialization behavior, default constructors, null handling

## Metadata

**Confidence breakdown:**
- Standard stack (UI framework, validation, HTTP, serialization): **HIGH** — all verified in Phase 9 code and CLAUDE.md
- Architecture patterns (form structure, list mutations, API error handling): **HIGH** — based on Login.razor (Phase 9) and API endpoint contracts
- Pitfalls (serialization round-trip, token expiry, EditForm list binding): **HIGH** — common Blazor/ASP.NET patterns, precedent in existing Login.razor
- Code examples (EditForm with nested lists, error handling): **MEDIUM-HIGH** — adapted from Login.razor, not yet tested in Phase 10 context
- Environment availability: **HIGH** — all dependencies already in IronMonkey.Web.csproj

**Research date:** 2026-04-01
**Valid until:** 2026-04-30 (stable Blazor/ASP.NET, no major API changes expected)
