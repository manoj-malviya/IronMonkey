# Phase 13: System Configuration UI - Research

**Researched:** 2026-04-01
**Domain:** Blazor Server admin UI with inline editing, tabbed pages, and CRUD API integration
**Confidence:** HIGH

## Summary

Phase 13 implements a single admin configuration page at `/admin/configuration` with 4 tabs for managing pipeline stages, custom fields, lead routing, and workflow rules. This phase requires both **backend endpoint gap closure** (4 missing DELETE/UPDATE endpoints) and **frontend Blazor UI implementation** with inline editing patterns not yet used in the project.

The backend is nearly complete: all GET/POST/PUT endpoints exist, but 4 critical delete/update operations are missing. The frontend will follow established patterns from Phases 9-12 (TenantManagement tabbed navigation, UserList CRUD, form validation) adapted for inline row editing instead of dedicated pages.

**Primary recommendation:** Decompose this phase into 2 parallel waves: (1) Create 4 missing backend endpoints (~3 tasks), (2) Build 4-tab Blazor UI with inline editing patterns (~4 tasks). The UI can proceed independently once endpoints are stubbed, enabling progress in parallel.

---

## User Constraints (from CONTEXT.md)

### Locked Decisions

**Page Organization:**
- Single page at `/admin/configuration` with 4 tabs: Pipeline Stages, Custom Fields, Routing, Workflow Rules
- Client-side tab switching (no navigation) — follows TenantManagement and RecipePreview tabbed pattern
- Default tab is "Pipeline Stages" (most commonly configured)
- All editing is inline — no separate create/edit pages for any config items

**Pipeline Stages Tab (CFUI-01):**
- List of stages showing Name, Order, StageType (Entry/Active/ClosedWon/ClosedLost), Actions
- Up/down arrow buttons per row for reordering — saves updated order via PUT endpoint
- "+ Add Stage" button appends an editable row at bottom — inline Name input + StageType dropdown, save button
- Click existing row to make it editable (Name and StageType) — save/cancel buttons appear
- Delete button per row with confirmation — removes stage
- Empty state: "No pipeline stages configured. Add your first stage to get started."

**Custom Fields Tab (CFUI-02):**
- List of fields showing Name, Type (Text/Number/Date/Select/Boolean), IsRequired badge, Options (for Select type), Actions
- "+ Add Field" button appends editable row — Name input, Type dropdown, IsRequired toggle, Options input (comma-separated, shown only for Select type)
- Click existing row to edit — same fields as create
- Delete button per row with confirmation
- Empty state: "No custom fields defined. Add fields to capture tenant-specific lead data."

**Routing Configuration Tab (CFUI-03):**
- Mode selector at top: radio buttons for "Round-Robin" or "Territory-Based"
- Form fields change based on selected mode
- Round-Robin mode: just the mode selection (no additional config needed — backend handles assignment)
- Territory mode: JSON config textarea for territory rules (matches ConfigureRoutingEndpoint contract)
- Single "Save Configuration" button at bottom
- Current config loaded from GET /api/routing-config on tab open

**Workflow Rules Tab (CFUI-04):**
- Table columns: Name/Description, TriggerType, ActionType, IsActive (toggle), Actions (Edit/Delete)
- Structured form inputs for rule editing: dropdowns for TriggerType and ActionType, text inputs for Conditions JSON and ActionConfig JSON
- "+ Add Rule" button opens inline editable section (not a new page)
- Click existing rule row to expand editable form below it — save/cancel buttons
- IsActive toggle per rule — immediate POST to toggle endpoint
- Delete button with confirmation
- Empty state: "No workflow rules configured. Create rules to automate your lead pipeline."

### Claude's Discretion

- Exact Tailwind styling for inline edit rows, toggle switches, confirmation dialogs
- Loading state approach for each tab
- Toast/notification for save success/failure
- Animation for expanding/collapsing inline edit rows
- Tab icons (if any)
- Exact JSON textarea sizing and placeholder text

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope.

---

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| CFUI-01 | Admin can view and manage pipeline stages (create, edit, reorder, delete) | Backend: ListPipelineStagesEndpoint, CreatePipelineStageEndpoint, UpdatePipelineStageEndpoint exist; DeletePipelineStageEndpoint missing. PipelineStage entity has Deactivate() and SetStageType() methods. Frontend: Inline row editing pattern with reorder arrows, delete confirmation. |
| CFUI-02 | Admin can view and manage custom field definitions (create, edit, delete) | Backend: ListCustomFieldsEndpoint, CreateCustomFieldEndpoint exist; UpdateCustomFieldEndpoint and DeleteCustomFieldEndpoint both missing. CustomFieldDefinition entity supports 7 field types (Text, Number, Date, Dropdown, MultiSelect, Currency, Boolean). Frontend: Type-aware optional Options field (shown only for Dropdown/MultiSelect). |
| CFUI-03 | Admin can view and manage lead routing configuration (round-robin, territory rules) | Backend: GetRoutingConfigEndpoint and ConfigureRoutingEndpoint exist; upsert pattern used. RoutingStrategy enum: RoundRobin, Territory. RoutingDimension enum: LeadSource, CustomField. TerritoryMapJson stored as JSONB. Frontend: Mode radio selector with conditional rendering based on selection. Territory mode shows JSON textarea. |
| CFUI-04 | Admin can view and manage workflow rules (triggers, conditions, actions) | Backend: ListWorkflowRulesEndpoint, CreateWorkflowRuleEndpoint, UpdateWorkflowRuleEndpoint (+ toggle) exist; DeleteWorkflowRuleEndpoint missing. WorkflowTrigger enum: FieldChange, StatusChange, TimeElapsed. ConditionJson and ActionJson stored as JSONB. Frontend: TriggerType/ActionType dropdowns, JSON textareas for conditions/config, IsActive toggle with immediate save. |

---

## Standard Stack

### Core Backend API Pattern
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| ASP.NET Core | 10.0 | Minimal API endpoint framework | Project standard, typed results pattern |
| Entity Framework Core | 10.0.5 | Tenant DB context factory | Multi-tenant isolation, JSONB support |
| Npgsql | 10.0.1 | PostgreSQL driver | JSONB query support for RoutingConfig, ConditionJson, ActionJson |
| FluentValidation | — | Request validation | Used in CustomFields endpoint for enum validation |

### Core Frontend Stack
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Blazor Server | .NET 10.0 | Interactive UI framework | Project standard |
| Razor Components | — | Page/component markup | @page, @code blocks, event binding |
| Tailwind CSS v4 | via standalone CLI | Styling utility classes | Project standard, MSBuild-integrated compilation |
| HttpClientFactory | .NET built-in | HTTP client management | Injected named client "AdminApi" with BearerTokenHandler |

### Supporting Frontend Patterns
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| EditForm + Input components | Blazor built-in | Form binding and validation | Routing tab (structured form) |
| ProtectedSessionStorage | Blazor built-in | JWT token persistence | Already wired; UI reads token via AdminAuthenticationStateProvider |
| BearerTokenHandler | Custom (IronMonkey) | Auto-inject JWT on API calls | AdminApi calls automatically authenticated |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Inline row editing | Dedicated create/edit pages | Decision locked: inline editing only (D-04) |
| Tailwind utilities | Component library (MudBlazor, etc.) | Decision locked: native Razor + Tailwind (PROJECT.md) |
| Form validation | Custom client-side logic | EditForm with data annotations sufficient for structured form (routing tab) |
| Toast notifications | No feedback | Claude's discretion — choose notification pattern (in-page banner vs. toast) |

**Version verification:**
- ASP.NET Core 10.0: Current LTS, matches project TargetFramework
- EF Core 10.0.5: Confirmed in IronMonkey.ApiService.csproj
- Npgsql 10.0.1: Confirmed in IronMonkey.ApiService.csproj (major version match with EF Core required)
- Blazor Server: Built into .NET 10.0
- Tailwind v4: Already integrated via standalone CLI (Phase 9)

---

## Backend Endpoint Contracts (Critical Gaps)

All existing endpoints follow the same pattern: Minimal API with `IEndpoint` interface, typed `Results<>` return type, tenant scoping via `ITenantService` and `ITenantDbContextFactory`.

### Existing Endpoints (Verified to Exist)

**Pipeline Stages:**
- GET `/api/pipeline-stages` — ListPipelineStagesEndpoint — returns list of stages for tenant
- POST `/api/pipeline-stages` — CreatePipelineStageEndpoint — creates stage with Name, Order, StageType
- PUT `/api/pipeline-stages/{id}` — UpdatePipelineStageEndpoint — updates Name, Order; checks order uniqueness

**Custom Fields:**
- GET `/api/custom-fields` — ListCustomFieldsEndpoint — returns list of field definitions for tenant
- POST `/api/custom-fields` — CreateCustomFieldEndpoint — creates field with FieldName, FieldType, IsRequired, Options (optional)

**Routing:**
- GET `/api/routing-config` — GetRoutingConfigEndpoint — returns current routing config (upsert pattern: creates if missing)
- POST `/api/routing-config` — ConfigureRoutingEndpoint — sets Strategy, Dimension, TerritoryMapJson, CustomFieldKey

**Workflow Rules:**
- GET `/api/workflow-rules` — ListWorkflowRulesEndpoint — returns rules for tenant
- POST `/api/workflow-rules` — CreateWorkflowRuleEndpoint — creates rule with Name, Trigger, ConditionJson, ActionJson
- PUT `/api/workflow-rules/{ruleId}` — UpdateWorkflowRuleEndpoint — updates rule definition
- POST `/api/workflow-rules/{ruleId}/toggle` — UpdateWorkflowRuleEndpoint.Toggle — toggles IsActive state

### Missing Endpoints (MUST be Created)

**1. DELETE Pipeline Stage**
```
DELETE /api/pipeline-stages/{id}
Response: Ok<DeleteResponse> | NotFound

Expected contract:
- Input: id (guid)
- Output: { success: bool, message: string? }
- Behavior: Soft-delete (set IsActive = false) or hard-delete (remove row)
- Validation: Check no leads reference this stage (if hard-delete)
```
**Note:** PipelineStage.cs has `Deactivate()` method, suggesting soft-delete is preferred. Verify if referential constraints exist on Lead.StageId.

**2. UPDATE Custom Field**
```
PUT /api/custom-fields/{id}
Request: { FieldName: string, FieldType: string, IsRequired: bool, Options?: string[] }
Response: Ok<Response> | NotFound | BadRequest

Expected contract:
- Mirrors CreateCustomFieldEndpoint.Request
- Output: { Id, FieldName, FieldType, IsRequired, Options }
- Validation: Same as create (enum check, options required for Dropdown/MultiSelect)
- Behavior: Update definition; no data migration (field values already stored as JSON)
```

**3. DELETE Custom Field**
```
DELETE /api/custom-fields/{id}
Response: Ok<DeleteResponse> | NotFound | Conflict

Expected contract:
- Output: { success: bool, message: string? }
- Validation: Check no leads have values for this field (or allow orphaned values?)
- Behavior: Soft-delete (set IsRequired = false + mark deleted?) or hard-delete
```
**Note:** No IsActive field on CustomFieldDefinition. Check migration history for soft-delete flag. If none exists, hard-delete may be expected.

**4. DELETE Workflow Rule**
```
DELETE /api/workflow-rules/{id}
Response: Ok<DeleteResponse> | NotFound

Expected contract:
- Output: { success: bool, message: string? }
- Behavior: Hard-delete (WorkflowRule has no IsActive soft-delete, unlike PipelineStage)
- Reasoning: Rules are not historical data; deletion is immediate
```

**Implementation Priority:** Create these 4 endpoints before frontend development to unblock UI tasks.

---

## Architecture Patterns

### Recommended Page Structure

```
IronMonkey.Web/Components/Pages/Admin/Configuration/
├── SystemConfiguration.razor         # Main page with tabs + state management
├── Shared/
│   ├── PipelineStagesTab.razor       # (Optional: extracted component)
│   ├── CustomFieldsTab.razor         # (Optional: extracted component)
│   ├── RoutingTab.razor              # (Optional: extracted component)
│   └── WorkflowRulesTab.razor        # (Optional: extracted component)
```

**Recommendation:** Start with all 4 tabs in SystemConfiguration.razor (following TenantManagement.razor pattern — single-file implementation). Extract to `Shared/` components only if file size exceeds ~500 lines or logic becomes unmaintainable.

### Tab-Based State Management Pattern

Established in Phase 11 (TenantManagement.razor):

```csharp
@code {
    private string _activeTab = "Pipeline Stages";  // Default per D-03
    private List<string> _tabs = new() { "Pipeline Stages", "Custom Fields", "Routing", "Workflow Rules" };
    
    // Tab-specific state
    private List<PipelineStageDto> _stages = new();
    private List<CustomFieldDto> _fields = new();
    private RoutingConfigDto? _routingConfig = null;
    private List<WorkflowRuleDto> _rules = new();
    
    // Global loading/error state
    private bool _isLoading = false;
    private string? _errorBanner = null;
    private string? _successMessage = null;
    
    // Inline editing state (one row at a time)
    private Guid? _editingId = null;  // Which row is being edited
    private [Type]Dto? _editingModel = null;  // Temporary model for in-flight edits
}
```

### Inline Row Editing Pattern

**Design principle:** Only one row editable at a time (prevents simultaneous saves, simpler state).

**State machine:**
1. **View mode:** Show table rows with data; click row → switch to edit mode
2. **Edit mode:** Show editable inputs for clicked row; Save/Cancel buttons
3. **Creating:** Show empty editable row at bottom; Save → POST → add to list
4. **Saving:** Disable buttons, show "Saving..." feedback
5. **Error:** Show error banner; stay in edit mode; user can retry

**Example: Pipeline Stages tab**

```html
<!-- View Mode: Table -->
@foreach (var stage in _stages)
{
    <tr @key="stage.Id" class="@(_editingId == stage.Id ? "bg-indigo-50" : "")">
        <!-- Readonly data cells (hidden when editing) -->
        @if (_editingId != stage.Id)
        {
            <td class="px-4 py-3">@stage.Name</td>
            <td class="px-4 py-3">@stage.StageType</td>
            <td class="px-4 py-3 space-x-2">
                <button @onclick="() => ReorderUp(stage.Id)" type="button">↑</button>
                <button @onclick="() => ReorderDown(stage.Id)" type="button">↓</button>
                <button @onclick="() => EditStage(stage)" type="button">Edit</button>
                <button @onclick="() => ConfirmDelete(stage.Id)" type="button">Delete</button>
            </td>
        }
        
        <!-- Edit Mode: Input fields (shown when _editingId == stage.Id) -->
        @if (_editingId == stage.Id && _editingModel != null)
        {
            <td class="px-4 py-3">
                <input type="text" @bind="_editingModel.Name" class="form-input" />
            </td>
            <td class="px-4 py-3">
                <select @bind="_editingModel.StageType" class="form-select">
                    @foreach (var type in Enum.GetValues<StageType>())
                    {
                        <option value="@type">@type</option>
                    }
                </select>
            </td>
            <td class="px-4 py-3 space-x-2">
                <button @onclick="SaveEditAsync" type="button" disabled="@_isSaving">Save</button>
                <button @onclick="CancelEdit" type="button">Cancel</button>
            </td>
        }
    </tr>
}

<!-- Create Mode: Editable row at bottom -->
@if (_editingId == null && _creatingNew)
{
    <tr class="bg-indigo-50">
        <td class="px-4 py-3">
            <input type="text" @bind="_newStageModel.Name" placeholder="Stage name" class="form-input" />
        </td>
        <td class="px-4 py-3">
            <select @bind="_newStageModel.StageType" class="form-select">
                @foreach (var type in Enum.GetValues<StageType>())
                {
                    <option value="@type">@type</option>
                }
            </select>
        </td>
        <td class="px-4 py-3 space-x-2">
            <button @onclick="SaveNewAsync" type="button" disabled="@_isSaving">Create</button>
            <button @onclick="CancelCreate" type="button">Cancel</button>
        </td>
    </tr>
}
```

### API Call Pattern (Established in Phases 9-12)

```csharp
private async Task SaveEditAsync()
{
    _isSaving = true;
    _errorBanner = null;
    
    try
    {
        var client = HttpClientFactory.CreateClient("AdminApi");
        var response = await client.PutAsJsonAsync($"/api/pipeline-stages/{_editingId}", 
            new { Name = _editingModel.Name, Order = _editingModel.Order });
        
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<PipelineStageDto>();
            // Update list in-place
            var idx = _stages.FindIndex(s => s.Id == _editingId);
            if (idx >= 0) _stages[idx] = result;
            _editingId = null;
            _editingModel = null;
            _successMessage = "Stage updated successfully";
        }
        else
        {
            _errorBanner = $"Failed to save: {response.StatusCode}";
        }
    }
    catch (Exception ex)
    {
        _errorBanner = $"Error: {ex.Message}";
    }
    finally
    {
        _isSaving = false;
    }
}
```

### Reorder Pattern (Pipeline Stages)

D-06 requires up/down arrows that save order via PUT. Implementation:

```csharp
private async Task ReorderUp(Guid stageId)
{
    var currentIdx = _stages.FindIndex(s => s.Id == stageId);
    if (currentIdx <= 0) return;  // Can't move first item up
    
    var current = _stages[currentIdx];
    var above = _stages[currentIdx - 1];
    
    // Swap orders in memory
    (current.Order, above.Order) = (above.Order, current.Order);
    
    // Persist both updates
    var client = HttpClientFactory.CreateClient("AdminApi");
    var t1 = client.PutAsJsonAsync($"/api/pipeline-stages/{current.Id}", new { Name = current.Name, Order = current.Order });
    var t2 = client.PutAsJsonAsync($"/api/pipeline-stages/{above.Id}", new { Name = above.Name, Order = above.Order });
    
    await Task.WhenAll(t1, t2);
    StateHasChanged();  // Refresh UI
}
```

### Confirmation Dialog Pattern (Established in Phase 12)

D-09, D-14, D-27 require delete confirmations:

```html
<!-- Modal: Delete Confirmation -->
@if (_showDeleteConfirm)
{
    <div class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
        <div class="bg-white rounded-lg p-6 max-w-sm">
            <h3 class="text-lg font-semibold text-slate-900">Confirm Delete</h3>
            <p class="mt-2 text-sm text-slate-600">This action cannot be undone.</p>
            <div class="mt-6 flex gap-3 justify-end">
                <button @onclick="CancelDelete" type="button" class="px-4 py-2 text-slate-700 hover:bg-slate-100">Cancel</button>
                <button @onclick="ConfirmDeleteAsync" type="button" class="px-4 py-2 bg-red-600 text-white hover:bg-red-700" disabled="@_isDeleting">
                    @(_isDeleting ? "Deleting..." : "Delete")
                </button>
            </div>
        </div>
    </div>
}

@code {
    private bool _showDeleteConfirm = false;
    private Guid? _deleteTargetId = null;
    private bool _isDeleting = false;
    
    private void ConfirmDelete(Guid id)
    {
        _deleteTargetId = id;
        _showDeleteConfirm = true;
    }
    
    private async Task ConfirmDeleteAsync()
    {
        _isDeleting = true;
        try
        {
            var client = HttpClientFactory.CreateClient("AdminApi");
            var response = await client.DeleteAsync($"/api/pipeline-stages/{_deleteTargetId}");
            
            if (response.IsSuccessStatusCode)
            {
                _stages.RemoveAll(s => s.Id == _deleteTargetId);
                _successMessage = "Stage deleted";
            }
            else
            {
                _errorBanner = $"Delete failed: {response.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            _errorBanner = $"Error: {ex.Message}";
        }
        finally
        {
            _isDeleting = false;
            _showDeleteConfirm = false;
            _deleteTargetId = null;
        }
    }
}
```

### Custom Fields Type-Aware Options (D-12)

Options field is only relevant for Dropdown/MultiSelect types:

```html
<div>
    <label class="block text-sm font-medium text-slate-700">Type</label>
    <select @bind="_editingModel.FieldType" @onchange="OnFieldTypeChange" class="form-select">
        @foreach (var type in Enum.GetValues<CustomFieldType>())
        {
            <option value="@type">@type</option>
        }
    </select>
</div>

<!-- Conditional: Show options input only for Dropdown/MultiSelect -->
@if (_editingModel.FieldType is CustomFieldType.Dropdown or CustomFieldType.MultiSelect)
{
    <div>
        <label class="block text-sm font-medium text-slate-700">Options (comma-separated)</label>
        <input type="text" 
               value="@string.Join(", ", _editingModel.Options)" 
               @onchange="@((ChangeEventArgs e) => ParseOptions(e.Value?.ToString()))"
               placeholder="Option A, Option B, Option C"
               class="form-input" />
    </div>
}

@code {
    private void OnFieldTypeChange(ChangeEventArgs e)
    {
        _editingModel.FieldType = (CustomFieldType)e.Value;
        // Clear options if switching to non-Dropdown type
        if (_editingModel.FieldType is not (CustomFieldType.Dropdown or CustomFieldType.MultiSelect))
        {
            _editingModel.Options = new();
        }
    }
    
    private void ParseOptions(string? input)
    {
        _editingModel.Options = input?
            .Split(',')
            .Select(o => o.Trim())
            .Where(o => !string.IsNullOrEmpty(o))
            .ToList() ?? new();
    }
}
```

### Routing Tab: Conditional Form Fields (D-16–D-20)

Routing config is special: it's a upsert (one per tenant). Mode selector changes form fields:

```html
<!-- Mode Selector -->
<fieldset>
    <legend class="text-sm font-semibold text-slate-900">Routing Strategy</legend>
    <div class="mt-4 space-y-3">
        <label class="flex items-center gap-3">
            <input type="radio" name="strategy" value="RoundRobin" 
                   @onchange="@((ChangeEventArgs e) => _routingModel.Strategy = e.Value.ToString())"
                   checked="@(_routingModel.Strategy == "RoundRobin")" />
            <span class="text-sm text-slate-700">Round-Robin (assign to next available agent)</span>
        </label>
        <label class="flex items-center gap-3">
            <input type="radio" name="strategy" value="Territory" 
                   @onchange="@((ChangeEventArgs e) => _routingModel.Strategy = e.Value.ToString())"
                   checked="@(_routingModel.Strategy == "Territory")" />
            <span class="text-sm text-slate-700">Territory-Based (custom rules)</span>
        </label>
    </div>
</fieldset>

<!-- Conditional: Territory Config (show only if Territory selected) -->
@if (_routingModel.Strategy == "Territory")
{
    <div class="mt-6">
        <label class="block text-sm font-semibold text-slate-900">Territory Configuration (JSON)</label>
        <textarea @bind="_routingModel.TerritoryMapJson" 
                  placeholder='{"region": "north", "agents": ["alice@acme.com", "bob@acme.com"]}'
                  class="form-textarea w-full mt-2 h-48 font-mono text-xs" />
        <p class="mt-2 text-xs text-slate-500">Define territory-to-agent mapping as JSON</p>
    </div>
}

<!-- Save Button (always shown) -->
<button @onclick="SaveRoutingAsync" type="button" class="mt-6 px-4 py-2 bg-indigo-600 text-white" disabled="@_isSaving">
    @(_isSaving ? "Saving..." : "Save Configuration")
</button>
```

### Workflow Rules: Expandable Row Pattern (D-24–D-25)

Workflow rules use a different pattern: a table row expands into an editable form below it.

```html
<!-- Workflow Rules Table -->
<table class="w-full">
    <thead>
        <tr>
            <th>Name</th>
            <th>Trigger</th>
            <th>Action</th>
            <th>Active</th>
            <th>Actions</th>
        </tr>
    </thead>
    <tbody>
        @foreach (var rule in _rules)
        {
            <!-- Summary Row -->
            <tr @key="rule.Id" class="border-b hover:bg-slate-50"
                style="cursor: pointer" @onclick="() => ToggleRuleEditor(rule.Id)">
                <td class="px-4 py-3">@rule.Name</td>
                <td class="px-4 py-3">@rule.Trigger</td>
                <td class="px-4 py-3">@rule.ActionType</td>
                <td class="px-4 py-3">
                    <!-- Toggle: IsActive (saves immediately per D-26) -->
                    <input type="checkbox" @checked="rule.IsActive" 
                           @onchange="@((ChangeEventArgs e) => ToggleRuleActiveAsync(rule.Id, (bool)e.Value))" />
                </td>
                <td class="px-4 py-3 space-x-2">
                    <button @onclick="@(() => ConfirmDeleteRule(rule.Id))" type="button" class="text-red-600">Delete</button>
                </td>
            </tr>
            
            <!-- Expandable Editor Row (shown when _editingRuleId == rule.Id) -->
            @if (_editingRuleId == rule.Id)
            {
                <tr class="bg-indigo-50">
                    <td colspan="5" class="px-4 py-4">
                        <div class="space-y-4">
                            <div>
                                <label class="block text-sm font-medium">Name</label>
                                <input type="text" @bind="_editingRuleModel.Name" class="form-input w-full" />
                            </div>
                            <div>
                                <label class="block text-sm font-medium">Trigger Type</label>
                                <select @bind="_editingRuleModel.Trigger" class="form-select w-full">
                                    @foreach (var trigger in Enum.GetValues<WorkflowTrigger>())
                                    {
                                        <option value="@trigger">@trigger</option>
                                    }
                                </select>
                            </div>
                            <div>
                                <label class="block text-sm font-medium">Conditions (JSON)</label>
                                <textarea @bind="_editingRuleModel.ConditionJson" class="form-textarea w-full h-24 font-mono text-xs" />
                            </div>
                            <div>
                                <label class="block text-sm font-medium">Action Configuration (JSON)</label>
                                <textarea @bind="_editingRuleModel.ActionJson" class="form-textarea w-full h-24 font-mono text-xs" />
                            </div>
                            <div class="flex gap-3 justify-end">
                                <button @onclick="SaveRuleAsync" type="button" disabled="@_isSaving">Save</button>
                                <button @onclick="CancelRuleEdit" type="button">Cancel</button>
                            </div>
                        </div>
                    </td>
                </tr>
            }
        }
    </tbody>
</table>

<!-- Create New Rule: Inline form at bottom -->
@if (_creatingNewRule)
{
    <div class="mt-6 p-4 bg-indigo-50 rounded-lg space-y-4">
        <h3 class="font-semibold text-slate-900">New Workflow Rule</h3>
        <!-- Same form fields as edit above -->
        <div class="flex gap-3 justify-end">
            <button @onclick="SaveNewRuleAsync" type="button">Create</button>
            <button @onclick="CancelNewRule" type="button">Cancel</button>
        </div>
    </div>
}
```

### Empty States (D-10, D-15, D-28)

For each tab, show empty state when list is empty:

```html
@if (_stages.Count == 0 && !_isLoading)
{
    <div class="rounded-xl border border-dashed border-slate-300 bg-slate-50 py-16 text-center">
        <p class="text-slate-600">No pipeline stages configured. Add your first stage to get started.</p>
        <button @onclick="StartCreatingStage" class="mt-4 px-4 py-2 bg-indigo-600 text-white rounded-lg">
            + Add Stage
        </button>
    </div>
}
```

### Error and Success Messaging

Global banners at top of page:

```html
<!-- Error Banner -->
@if (!string.IsNullOrEmpty(_errorBanner))
{
    <div class="rounded-lg bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
        @_errorBanner
    </div>
}

<!-- Success Banner -->
@if (!string.IsNullOrEmpty(_successMessage))
{
    <div class="rounded-lg bg-green-50 border border-green-200 px-4 py-3 text-sm text-green-700">
        @_successMessage
        <button type="button" @onclick="() => _successMessage = null" class="ml-2 font-semibold">Dismiss</button>
    </div>
}
```

Clear messages after 5 seconds:

```csharp
private async Task ShowSuccess(string message)
{
    _successMessage = message;
    await Task.Delay(5000);
    _successMessage = null;
}
```

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| **Multi-tab page state** | Custom tab switching logic, multiple view modes per tab | Follow TenantManagement.razor pattern (_activeTab string, tab-specific `@if` blocks) | Already battle-tested in project; avoiding race conditions, render timing bugs |
| **Inline row editing** | Ad-hoc edit mode flags per row | Single `_editingId` + `_editingModel` pattern | Prevents simultaneous edits, simplifies state; complex to retrofit later |
| **Form validation on create/edit** | Manual null/length checks | EditForm + DataAnnotations or FluentValidation in API | Validation should live server-side for security; client-side is UI polish |
| **API error recovery** | Hardcoded error messages | Read response.StatusCode + response body | Different endpoints may return different error structures; generic message unacceptable |
| **Delete with confirmation** | Immediate deletion on button click | Modal overlay with confirm button | Prevents accidental data loss; standard UX pattern |
| **Reordering persistence** | Drag-and-drop UI | Up/down arrow buttons with PUT calls per D-06 | Decision locked; reorder is explicit, not drag-based |
| **JSON textarea validation** | Accept any text, save invalid JSON | Validate JSON structure before POST/PUT | Backend will reject; surface error message to user early |
| **ToggleRuleActiveAsync immediate save** | Optimistic update only | Optimistic update + API call in background | If toggle fails, checkbox state will flicker; handle error gracefully |

**Key insight:** Inline editing seems simple but state management complexity grows quickly. The single-`_editingId` pattern prevents subtle bugs where two rows appear editable at once, or Cancel doesn't properly reset form state.

---

## Common Pitfalls

### Pitfall 1: Inline Edit State Pollution

**What goes wrong:** Multiple rows allow simultaneous editing. User edits row A, clicks row B, both rows now show as editable. Cancel on B doesn't reset A. Save on A uses stale `_editingModel` from B.

**Why it happens:** Trying to support multiple concurrent edits without explicit state machine.

**How to avoid:** 
- Enforce single-`_editingId` invariant: only one row editable at a time
- Each edit starts fresh: `_editingModel = new();` + copy from list item
- Cancel unconditionally clears: `_editingId = null; _editingModel = null;`

**Warning signs:**
- Two rows visually highlighted at once
- Save button calls API with wrong data
- Tests that click edit on two different rows without cancel in between

### Pitfall 2: Forgot to Update List After Save

**What goes wrong:** User edits a field, clicks Save, API succeeds, but the table still shows old data. User refreshes page to see change.

**Why it happens:** Saving calls API but doesn't update `_stages` list in memory.

**How to avoid:**
- After successful PUT: find item in list by ID, replace with response DTO
- Or: re-fetch entire list from GET endpoint (slower, but safer)
- Test: edit a stage name, verify table updates without page refresh

**Warning signs:**
- UI doesn't reflect API response
- Network tab shows 200 response, but UI unchanged
- Stale data visible until user refreshes

### Pitfall 3: IsActive Toggle Fails Silently

**What goes wrong:** User toggles IsActive checkbox, nothing happens. API call fails (network error, 500, etc.). Checkbox stays visually toggled but actual state unchanged on server.

**Why it happens:** Optimistic UI update without error handling.

**How to avoid:**
- Check response success before keeping UI change
- If failed: revert checkbox to original state + show error banner
- Pattern: `rule.IsActive = newValue; await API; if (failed) rule.IsActive = !newValue;`

**Warning signs:**
- Toggle doesn't sync with server state
- Network errors go unnoticed
- Rules behave inconsistently (one rule active, another inactive, but toggle state opposite)

### Pitfall 4: JSON Textarea Accepts Invalid JSON

**What goes wrong:** User types malformed JSON in Conditions/ActionJson textarea (missing quote, trailing comma). Clicks Save. API rejects with 400 Bad Request, but error message is generic HTTP error, not "Invalid JSON".

**Why it happens:** No client-side validation on free-form JSON fields.

**How to avoid:**
- On blur or before save: Try to parse JSON, show validation error if fails
- Pattern: `TryJsonParse(input, out _) ? "Valid" : "Invalid JSON"`
- Show hint: "Must be valid JSON. Example: {…}"

**Warning signs:**
- Validation errors from backend shown to users (should be caught client-side)
- Confusion: "Why did my JSON fail?"

### Pitfall 5: Reorder API Calls Race

**What goes wrong:** User clicks up arrow 5 times in rapid succession. Each click fires two PUT requests (swap current + above item). Requests arrive out of order. Final state is wrong (items in original order, or different wrong order).

**Why it happens:** No debouncing or disable-while-pending on reorder buttons.

**How to avoid:**
- Disable all reorder buttons while saving: `disabled="@_isSaving"`
- Only update UI after both API calls succeed
- Consider: "Lock" the list during reorder (dim, show spinner)

**Warning signs:**
- Rapid clicks cause final order different from expected
- Network tab shows requests out of sequence

### Pitfall 6: Custom Fields: Type Change Loses Options

**What goes wrong:** User creates field as Dropdown with options ["A", "B"]. Clicks edit. Changes Type to Text. Clicks Save. Field now Text, but old options are still in the entity. Switching back to Dropdown shows old options.

**Why it happens:** Clearing options only on type change in UI, not in API.

**How to avoid:**
- Clear options in code before PUT: `if (fieldType != Dropdown) model.Options = [];`
- Or: API endpoint clears options server-side if type isn't Dropdown

**Warning signs:**
- Switching field types back and forth preserves old options
- Test: create Dropdown field, change to Text, change back to Dropdown, options still there

### Pitfall 7: Forgot to Disable Save Button During Reorder

**What goes wrong:** User reorders a stage, then immediately clicks Edit on another stage. Edit uses stale order values. Reorder request completes, overwrites the edit.

**Why it happens:** Reorder doesn't set `_isSaving` flag, so Edit doesn't know a save is in-flight.

**How to avoid:**
- Set `_isSaving = true` before any API call (not just form saves)
- Disable all interactive buttons while `_isSaving`
- Clear flag only after all pending requests complete

**Warning signs:**
- Rapid clicks on reorder + edit causes data loss or inconsistency

### Pitfall 8: Routing Tab: Forgetting to Save Territory Config

**What goes wrong:** User selects Territory mode, pastes complex JSON, then accidentally clicks "Save Configuration" without filling in all required territory fields. API accepts it. Later, routing fails silently because config is incomplete.

**Why it happens:** No validation of TerritoryMapJson structure server-side.

**How to avoid:**
- Before save: validate TerritoryMapJson is not empty AND is valid JSON
- Show validation error if invalid
- Backend should also validate (don't trust client)

**Warning signs:**
- Malformed JSON gets saved to database
- Routing doesn't work; no error message (silent failure)

---

## Code Examples

### Example 1: Full Inline Edit Workflow (Pipeline Stages)

**Source:** Established pattern from TenantManagement.razor + UserList.razor

```csharp
@page "/admin/configuration"
@attribute [Authorize]
@using System.Collections.Generic
@inject IHttpClientFactory HttpClientFactory
@inject NavigationManager Nav

<PageTitle>System Configuration — IronMonkey Admin</PageTitle>

<div class="space-y-6">
    <h1 class="text-2xl font-bold text-slate-900">System Configuration</h1>
    
    <!-- Tab navigation -->
    <div class="border-b border-slate-200">
        <nav class="flex space-x-8">
            @foreach (var tab in _tabs)
            {
                <button type="button" @onclick="() => SelectTab(tab)"
                        class="@(tab == _activeTab
                            ? "border-b-2 border-indigo-600 text-indigo-600 font-medium"
                            : "text-slate-600 hover:text-slate-900") px-1 py-4 text-sm transition-colors">
                    @tab
                </button>
            }
        </nav>
    </div>
    
    <!-- Error/Success banners -->
    @if (!string.IsNullOrEmpty(_errorBanner))
    {
        <div class="rounded-lg bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
            @_errorBanner
        </div>
    }
    @if (!string.IsNullOrEmpty(_successMessage))
    {
        <div class="rounded-lg bg-green-50 border border-green-200 px-4 py-3 text-sm text-green-700">
            @_successMessage
        </div>
    }
    
    <!-- Tab Content: Pipeline Stages -->
    @if (_activeTab == "Pipeline Stages")
    {
        @if (_isLoading)
        {
            <p class="text-sm text-slate-500 py-12 text-center">Loading stages...</p>
        }
        else if (_stages.Count == 0)
        {
            <div class="rounded-xl border border-dashed border-slate-300 bg-slate-50 py-16 text-center">
                <p class="text-slate-600">No pipeline stages configured. Add your first stage to get started.</p>
                <button @onclick="StartCreatingStage"
                        class="mt-4 px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-500">
                    + Add Stage
                </button>
            </div>
        }
        else
        {
            <div class="overflow-x-auto rounded-lg border border-slate-200">
                <table class="w-full">
                    <thead class="bg-slate-50 border-b border-slate-200">
                        <tr>
                            <th class="px-6 py-3 text-left text-xs font-semibold text-slate-900 uppercase">Name</th>
                            <th class="px-6 py-3 text-left text-xs font-semibold text-slate-900 uppercase">Type</th>
                            <th class="px-6 py-3 text-right text-xs font-semibold text-slate-900 uppercase">Actions</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var stage in _stages)
                        {
                            <tr @key="stage.Id" class="border-b border-slate-100 hover:bg-slate-50 @(_editingId == stage.Id ? "bg-indigo-50" : "")">
                                @if (_editingId != stage.Id)
                                {
                                    <td class="px-6 py-4 text-sm text-slate-900">@stage.Name</td>
                                    <td class="px-6 py-4 text-sm text-slate-600">@stage.StageType</td>
                                    <td class="px-6 py-4 text-right space-x-2">
                                        <button @onclick="() => ReorderUpAsync(stage.Id)" type="button" disabled="@_isSaving"
                                                class="text-indigo-600 hover:text-indigo-700 disabled:opacity-50">↑</button>
                                        <button @onclick="() => ReorderDownAsync(stage.Id)" type="button" disabled="@_isSaving"
                                                class="text-indigo-600 hover:text-indigo-700 disabled:opacity-50">↓</button>
                                        <button @onclick="() => EditStage(stage)" type="button" disabled="@_isSaving"
                                                class="text-indigo-600 hover:text-indigo-700 disabled:opacity-50">Edit</button>
                                        <button @onclick="() => ConfirmDelete(stage.Id)" type="button" disabled="@_isSaving"
                                                class="text-red-600 hover:text-red-700 disabled:opacity-50">Delete</button>
                                    </td>
                                }
                                else if (_editingModel != null)
                                {
                                    <td class="px-6 py-4">
                                        <input type="text" @bind="_editingModel.Name" class="px-2 py-1 border border-slate-300 rounded" />
                                    </td>
                                    <td class="px-6 py-4">
                                        <select @bind="_editingModel.StageType" class="px-2 py-1 border border-slate-300 rounded">
                                            @foreach (var type in Enum.GetValues<StageType>())
                                            {
                                                <option value="@type">@type</option>
                                            }
                                        </select>
                                    </td>
                                    <td class="px-6 py-4 text-right space-x-2">
                                        <button @onclick="SaveEditAsync" type="button" disabled="@_isSaving"
                                                class="text-indigo-600 hover:text-indigo-700 disabled:opacity-50">
                                            @(_isSaving ? "Saving..." : "Save")
                                        </button>
                                        <button @onclick="CancelEdit" type="button" disabled="@_isSaving"
                                                class="text-slate-600 hover:text-slate-700 disabled:opacity-50">Cancel</button>
                                    </td>
                                }
                            </tr>
                        }
                        
                        <!-- Create row -->
                        @if (_creatingNew && _newModel != null)
                        {
                            <tr class="bg-indigo-50 border-b border-slate-100">
                                <td class="px-6 py-4">
                                    <input type="text" @bind="_newModel.Name" placeholder="Stage name" class="px-2 py-1 border border-indigo-300 rounded w-full" />
                                </td>
                                <td class="px-6 py-4">
                                    <select @bind="_newModel.StageType" class="px-2 py-1 border border-indigo-300 rounded w-full">
                                        @foreach (var type in Enum.GetValues<StageType>())
                                        {
                                            <option value="@type">@type</option>
                                        }
                                    </select>
                                </td>
                                <td class="px-6 py-4 text-right space-x-2">
                                    <button @onclick="SaveNewAsync" type="button" disabled="@_isSaving"
                                            class="text-indigo-600 hover:text-indigo-700 disabled:opacity-50">
                                        @(_isSaving ? "Creating..." : "Create")
                                    </button>
                                    <button @onclick="CancelCreate" type="button" disabled="@_isSaving"
                                            class="text-slate-600 hover:text-slate-700 disabled:opacity-50">Cancel</button>
                                </td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
            
            @if (!_creatingNew)
            {
                <button @onclick="StartCreatingStage" type="button" class="mt-4 px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-500">
                    + Add Stage
                </button>
            }
        }
    }
    
    <!-- Other tabs (Custom Fields, Routing, Workflow Rules) similar pattern... -->
</div>

<!-- Delete Confirmation Modal -->
@if (_showDeleteConfirm)
{
    <div class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
        <div class="bg-white rounded-lg p-6 max-w-sm">
            <h3 class="text-lg font-semibold text-slate-900">Confirm Delete</h3>
            <p class="mt-2 text-sm text-slate-600">This action cannot be undone.</p>
            <div class="mt-6 flex gap-3 justify-end">
                <button @onclick="CancelDelete" type="button" class="px-4 py-2 text-slate-700 hover:bg-slate-100 rounded">
                    Cancel
                </button>
                <button @onclick="ConfirmDeleteAsync" type="button" disabled="@_isDeleting"
                        class="px-4 py-2 bg-red-600 text-white hover:bg-red-700 rounded disabled:opacity-50">
                    @(_isDeleting ? "Deleting..." : "Delete")
                </button>
            </div>
        </div>
    </div>
}

@code {
    private string _activeTab = "Pipeline Stages";
    private List<string> _tabs = new() { "Pipeline Stages", "Custom Fields", "Routing", "Workflow Rules" };
    
    // Stage state
    private List<PipelineStageDto> _stages = new();
    private Guid? _editingId = null;
    private PipelineStageEditModel? _editingModel = null;
    private bool _creatingNew = false;
    private PipelineStageEditModel? _newModel = null;
    
    // Global state
    private bool _isLoading = false;
    private bool _isSaving = false;
    private string? _errorBanner = null;
    private string? _successMessage = null;
    
    // Delete modal
    private bool _showDeleteConfirm = false;
    private Guid? _deleteTargetId = null;
    private bool _isDeleting = false;
    
    private record PipelineStageDto(Guid Id, string Name, int Order, string StageType, bool IsActive);
    private class PipelineStageEditModel { public string Name { get; set; } = ""; public string StageType { get; set; } = "Active"; }
    
    private enum StageType { Entry, Active, ClosedWon, ClosedLost }
    
    protected override async Task OnInitializedAsync()
    {
        await LoadStagesAsync();
    }
    
    private async Task LoadStagesAsync()
    {
        _isLoading = true;
        _errorBanner = null;
        try
        {
            var client = HttpClientFactory.CreateClient("AdminApi");
            var stages = await client.GetFromJsonAsync<List<PipelineStageDto>>("/api/pipeline-stages") ?? new();
            _stages = stages.OrderBy(s => s.Order).ToList();
        }
        catch (Exception ex)
        {
            _errorBanner = $"Failed to load stages: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }
    
    private void SelectTab(string tab)
    {
        CancelEdit();
        CancelCreate();
        _activeTab = tab;
    }
    
    private void EditStage(PipelineStageDto stage)
    {
        _editingId = stage.Id;
        _editingModel = new PipelineStageEditModel { Name = stage.Name, StageType = stage.StageType };
    }
    
    private void CancelEdit()
    {
        _editingId = null;
        _editingModel = null;
    }
    
    private async Task SaveEditAsync()
    {
        if (_editingId == null || _editingModel == null) return;
        
        _isSaving = true;
        _errorBanner = null;
        
        try
        {
            var client = HttpClientFactory.CreateClient("AdminApi");
            var stage = _stages.FirstOrDefault(s => s.Id == _editingId);
            var response = await client.PutAsJsonAsync($"/api/pipeline-stages/{_editingId}",
                new { Name = _editingModel.Name, Order = stage?.Order ?? 0 });
            
            if (response.IsSuccessStatusCode)
            {
                var updated = await response.Content.ReadFromJsonAsync<PipelineStageDto>();
                if (updated != null)
                {
                    var idx = _stages.FindIndex(s => s.Id == _editingId);
                    if (idx >= 0) _stages[idx] = updated;
                }
                _editingId = null;
                _editingModel = null;
                _successMessage = "Stage updated successfully";
                await ShowSuccessMessage(3000);
            }
            else
            {
                _errorBanner = $"Failed to save: {response.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            _errorBanner = $"Error: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
        }
    }
    
    private async Task ReorderUpAsync(Guid stageId)
    {
        var idx = _stages.FindIndex(s => s.Id == stageId);
        if (idx <= 0) return;
        
        _isSaving = true;
        try
        {
            var current = _stages[idx];
            var above = _stages[idx - 1];
            var client = HttpClientFactory.CreateClient("AdminApi");
            
            // Swap orders
            var newCurrOrder = above.Order;
            var newAboveOrder = current.Order;
            
            var t1 = client.PutAsJsonAsync($"/api/pipeline-stages/{current.Id}",
                new { Name = current.Name, Order = newCurrOrder });
            var t2 = client.PutAsJsonAsync($"/api/pipeline-stages/{above.Id}",
                new { Name = above.Name, Order = newAboveOrder });
            
            await Task.WhenAll(t1, t2);
            
            // Swap in memory
            (_stages[idx], _stages[idx - 1]) = (_stages[idx - 1], _stages[idx]);
            StateHasChanged();
        }
        catch (Exception ex)
        {
            _errorBanner = $"Reorder failed: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
        }
    }
    
    private async Task ReorderDownAsync(Guid stageId)
    {
        var idx = _stages.FindIndex(s => s.Id == stageId);
        if (idx >= _stages.Count - 1) return;
        
        _isSaving = true;
        try
        {
            var current = _stages[idx];
            var below = _stages[idx + 1];
            var client = HttpClientFactory.CreateClient("AdminApi");
            
            var newCurrOrder = below.Order;
            var newBelowOrder = current.Order;
            
            var t1 = client.PutAsJsonAsync($"/api/pipeline-stages/{current.Id}",
                new { Name = current.Name, Order = newCurrOrder });
            var t2 = client.PutAsJsonAsync($"/api/pipeline-stages/{below.Id}",
                new { Name = below.Name, Order = newBelowOrder });
            
            await Task.WhenAll(t1, t2);
            
            (_stages[idx], _stages[idx + 1]) = (_stages[idx + 1], _stages[idx]);
            StateHasChanged();
        }
        catch (Exception ex)
        {
            _errorBanner = $"Reorder failed: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
        }
    }
    
    private void StartCreatingStage()
    {
        _creatingNew = true;
        _newModel = new PipelineStageEditModel();
    }
    
    private void CancelCreate()
    {
        _creatingNew = false;
        _newModel = null;
    }
    
    private async Task SaveNewAsync()
    {
        if (_newModel == null) return;
        
        _isSaving = true;
        _errorBanner = null;
        
        try
        {
            var client = HttpClientFactory.CreateClient("AdminApi");
            var nextOrder = (_stages.Count == 0) ? 1 : _stages.Max(s => s.Order) + 1;
            
            var response = await client.PostAsJsonAsync("/api/pipeline-stages",
                new { Name = _newModel.Name, Order = nextOrder, StageType = _newModel.StageType });
            
            if (response.IsSuccessStatusCode)
            {
                var created = await response.Content.ReadFromJsonAsync<PipelineStageDto>();
                if (created != null)
                {
                    _stages.Add(created);
                    _stages = _stages.OrderBy(s => s.Order).ToList();
                }
                _creatingNew = false;
                _newModel = null;
                _successMessage = "Stage created successfully";
                await ShowSuccessMessage(3000);
            }
            else
            {
                _errorBanner = $"Failed to create: {response.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            _errorBanner = $"Error: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
        }
    }
    
    private void ConfirmDelete(Guid stageId)
    {
        _deleteTargetId = stageId;
        _showDeleteConfirm = true;
    }
    
    private void CancelDelete()
    {
        _showDeleteConfirm = false;
        _deleteTargetId = null;
    }
    
    private async Task ConfirmDeleteAsync()
    {
        if (_deleteTargetId == null) return;
        
        _isDeleting = true;
        try
        {
            var client = HttpClientFactory.CreateClient("AdminApi");
            var response = await client.DeleteAsync($"/api/pipeline-stages/{_deleteTargetId}");
            
            if (response.IsSuccessStatusCode)
            {
                _stages.RemoveAll(s => s.Id == _deleteTargetId);
                _successMessage = "Stage deleted";
                _showDeleteConfirm = false;
                _deleteTargetId = null;
                await ShowSuccessMessage(3000);
            }
            else
            {
                _errorBanner = $"Delete failed: {response.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            _errorBanner = $"Error: {ex.Message}";
        }
        finally
        {
            _isDeleting = false;
        }
    }
    
    private async Task ShowSuccessMessage(int delayMs = 5000)
    {
        await Task.Delay(delayMs);
        _successMessage = null;
    }
}
```

This example covers Pipeline Stages completely. Replicate the pattern for Custom Fields (with type-aware Options field), Routing (with mode-based conditional form), and Workflow Rules (with expandable row editor).

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Separate detail pages per entity (Phase 10) | Inline editing in tables (Phase 13) | Design decision (D-04) | Simpler navigation, more compact, but requires careful state management |
| Tab navigation with page loads | Client-side tab switching (Phase 11+) | Phase 11 (TenantManagement) | Faster UX, no full page reloads, but all tab data must fit in memory |
| Manual token refresh | ProtectedSessionStorage + BearerTokenHandler (Phase 9) | Phase 9 | Automatic JWT injection, no need for explicit auth handling in components |
| Tailwind CSS via npm pipeline | Standalone CLI (Phase 9) | Phase 9 | No Node.js dependency, MSBuild integration, simpler setup |

**Deprecated/outdated:**
- Razor Pages (.cshtml) — Project uses Blazor Server components instead
- Server-side form posts — Project uses Blazor event bindings + API calls
- Session-based auth — Project uses JWT in ProtectedSessionStorage

---

## Open Questions

1. **Delete Endpoint Behavior: Hard-Delete or Soft-Delete for Pipeline Stages?**
   - What we know: `PipelineStage.Deactivate()` method exists; suggests soft-delete pattern
   - What's unclear: Is there a migration with `IsDeleted` column? Do leads have FK constraint on StageId?
   - Recommendation: Verify schema, check if leads can exist without a stage. Hard-delete safer if FK enforced; soft-delete safer if leads must reference historical stages.

2. **Custom Fields: Delete or Mark Hidden?**
   - What we know: `CustomFieldDefinition` has no soft-delete flag like `IsDeleted`
   - What's unclear: Can a lead have a value for a deleted field? Should that value be preserved or orphaned?
   - Recommendation: Check if CustomFieldValue entities exist and have FK to CustomFieldDefinition. If yes, hard-delete may violate FK; soft-delete better.

3. **Reorder Order Values: Gaps or Sequential?**
   - What we know: UpdatePipelineStageEndpoint validates order uniqueness per tenant
   - What's unclear: Can orders be [1, 2, 5] (gaps)? Or must be [1, 2, 3] (sequential)?
   - Recommendation: Current reorder logic swaps orders directly. If sequential is required, may need renumbering logic.

4. **Routing Config: One per Tenant or One per Pipeline?**
   - What we know: ConfigureRoutingEndpoint uses upsert (one config per tenant via TenantId)
   - What's unclear: Design intent: global tenant routing, or per-pipeline routing?
   - Recommendation: Check if multiple RoutingConfig records per tenant exist. If not, current design is correct (one tenant-wide routing strategy).

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 |
| Config file | IronMonkey.Tests/IronMonkey.Tests.csproj |
| Quick run command | `dotnet test IronMonkey.Tests --filter "ClassName"` |
| Full suite command | `dotnet test IronMonkey.Tests` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| CFUI-01 | Admin creates pipeline stage via PUT endpoint | Integration | `dotnet test IronMonkey.Tests --filter "PipelineStage"` | ❌ Wave 0: No endpoint tests exist for reorder or delete |
| CFUI-01 | Admin reorders stages (up/down arrows) | Integration | Same | ❌ Wave 0: Reorder logic not tested |
| CFUI-01 | Admin deletes stage with confirmation | Integration | `dotnet test IronMonkey.Tests --filter "PipelineStage" --grep "Delete"` | ❌ Wave 0: DeletePipelineStageEndpoint doesn't exist |
| CFUI-02 | Admin updates custom field definition | Integration | `dotnet test IronMonkey.Tests --filter "CustomField" --grep "Update"` | ❌ Wave 0: UpdateCustomFieldEndpoint doesn't exist |
| CFUI-02 | Admin deletes custom field | Integration | Same | ❌ Wave 0: DeleteCustomFieldEndpoint doesn't exist |
| CFUI-03 | Admin switches routing strategy (round-robin ↔ territory) | Integration | `dotnet test IronMonkey.Tests --filter "Routing"` | ✅ GetRoutingConfigEndpoint, ConfigureRoutingEndpoint exist |
| CFUI-04 | Admin creates workflow rule with trigger/condition/action | Integration | `dotnet test IronMonkey.Tests --filter "WorkflowRule"` | ✅ CreateWorkflowRuleEndpoint tested |
| CFUI-04 | Admin updates workflow rule | Integration | Same | ✅ UpdateWorkflowRuleEndpoint exists |
| CFUI-04 | Admin toggles rule active/inactive | Integration | Same | ✅ Toggle endpoint exists |
| CFUI-04 | Admin deletes workflow rule | Integration | Same | ❌ Wave 0: DeleteWorkflowRuleEndpoint doesn't exist |

### Sampling Rate
- **Per task commit:** `dotnet test IronMonkey.Tests --filter "[EndpointName]Tests"` (test only changed endpoints)
- **Per wave merge:** `dotnet test IronMonkey.Tests` (all tests)
- **Phase gate:** Full suite green + Blazor UI smoke tested (click through all 4 tabs, verify no JS errors)

### Wave 0 Gaps

- [ ] `IronMonkey.ApiService/Features/Leads/PipelineStages/DeletePipelineStageEndpoint.cs` — DELETE /api/pipeline-stages/{id}
  - Test: `dotnet test IronMonkey.Tests --filter "DeletePipelineStageEndpoint"`
  - Covers: CFUI-01 delete behavior
  
- [ ] `IronMonkey.ApiService/Features/Leads/CustomFields/UpdateCustomFieldEndpoint.cs` — PUT /api/custom-fields/{id}
  - Test: `dotnet test IronMonkey.Tests --filter "UpdateCustomFieldEndpoint"`
  - Covers: CFUI-02 edit behavior
  
- [ ] `IronMonkey.ApiService/Features/Leads/CustomFields/DeleteCustomFieldEndpoint.cs` — DELETE /api/custom-fields/{id}
  - Test: `dotnet test IronMonkey.Tests --filter "DeleteCustomFieldEndpoint"`
  - Covers: CFUI-02 delete behavior
  
- [ ] `IronMonkey.ApiService/Features/Leads/Workflow/Rules/DeleteWorkflowRuleEndpoint.cs` — DELETE /api/workflow-rules/{id}
  - Test: `dotnet test IronMonkey.Tests --filter "DeleteWorkflowRuleEndpoint"`
  - Covers: CFUI-04 delete behavior
  
- [ ] `IronMonkey.Web/Components/Pages/Admin/Configuration/SystemConfiguration.razor` — Main page component
  - Manual smoke test: Load page, verify 4 tabs render, click each tab, verify no JS errors
  - Covers: All CFUI requirements (UI integration)

---

## Sources

### Primary (HIGH confidence)
- **IronMonkey codebase** (local files)
  - Verified existing endpoints: PipelineStages (List, Create, Update), CustomFields (List, Create), Routing (Get, Configure), WorkflowRules (List, Create, Update, Toggle)
  - Verified entity models: PipelineStage, CustomFieldDefinition, RoutingConfig, WorkflowRule
  - Verified UI patterns: TenantManagement.razor tabbed layout, UserList.razor CRUD pattern, RecipeEdit.razor form pattern
  - Verified data models: StageType enum, CustomFieldType enum, WorkflowTrigger enum, RoutingStrategy enum
  - Test framework: xUnit 2.9.3 with Testcontainers for PostgreSQL

- **CLAUDE.md** (project instructions)
  - Architecture: Multi-tenant isolation via CentralDbContext + TenantDbContextFactory
  - API pattern: Minimal API with IEndpoint interface, typed Results<> return type
  - Auth: JWT in ProtectedSessionStorage, BearerTokenHandler injects token on AdminApi calls
  - Testing: Docker required (Testcontainers), xUnit, Moq

- **CONTEXT.md** (Phase 13 decisions)
  - Locked: Single page /admin/configuration with 4 tabs, inline editing, no separate create/edit pages
  - Locked: Named endpoints needed; 4 backend gaps identified

### Secondary (MEDIUM confidence)
- **Phase 9-12 implemented patterns** (verified by file inspection)
  - TenantManagement.razor: Client-side tab switching, error/success banners
  - UserList.razor: Delete modal, CRUD buttons, deactivation pattern
  - RecipeEdit.razor: EditForm, optimistic updates, loading states
  - AdminSidebar.razor: Navigation links already include /admin/stages, /admin/fields, /admin/routing, /admin/workflows (routing ready)

- **.NET 10.0 / Blazor Server documentation**
  - Blazor event binding syntax (@onclick, @onchange, @bind)
  - EditForm + DataAnnotations for form validation
  - ProtectedSessionStorage for secure client-side storage
  - Built-in HttpClient for API calls

---

## Metadata

**Confidence breakdown:**
- **Standard Stack:** HIGH — All libraries verified in .csproj files, patterns tested in previous phases
- **Architecture Patterns:** HIGH — TenantManagement, UserList, RecipeEdit exist as working examples; patterns extrapolated with confidence
- **Backend Gaps:** HIGH — Glob searches confirmed 4 endpoints don't exist; endpoint contracts inferred from existing endpoints + entity models
- **UI Pitfalls:** MEDIUM — Inline editing is new pattern for project; recommendations based on general UI best practices + project conventions
- **Validation:** HIGH — xUnit + Testcontainers verified; test commands constructed from CLAUDE.md

**Research date:** 2026-04-01
**Valid until:** 2026-04-14 (stable domain, minor version changes unlikely in 2 weeks)

---

*Phase: 13-system-configuration-ui*
*Research completed: 2026-04-01*
