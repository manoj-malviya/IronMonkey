# Phase 11: Tenant Management UI - Research

**Researched:** 2026-04-01
**Domain:** Blazor Server admin UI with tabbed interface for tenant and signup request management
**Confidence:** HIGH

## Summary

Phase 11 builds a two-tab admin page at `/admin/tenants` to enable platform admins to monitor all tenants and action signup requests. The implementation leverages established Razor component patterns from Phase 9/10, client-side tab switching (no routing), and three new backend endpoints to support data retrieval. The UI layer uses native Razor components with Tailwind styling; the API layer extends the minimal endpoint pattern with data-only GET endpoints and an optimistic approval/rejection flow.

Key findings:
1. **Established patterns exist** — RecipeList.razor and RecipePreview.razor directly model the table structure, filtering, expandable rows, and tab switching needed
2. **Two backend endpoints missing** — No `GET /admin/tenants` or `GET /admin/signup/{id}` endpoint exists; both must be created as part of this phase
3. **Approval flow chains two APIs** — Approve triggers a POST, then manually triggers provision; on failure, UI shows "Provision" button for retry
4. **Test infrastructure ready** — xUnit with Testcontainers PostgreSQL; existing integration tests use `PostgreSqlFixture` pattern

**Primary recommendation:** Create tenant list UI as direct adaptation of RecipeList.razor, signup requests UI with expandable row pattern from RecipePreview tabs, implement backend GET endpoints following ApproveTenantEndpoint pattern (minimal, typed results), handle approval+provision chaining with optimistic UI updates.

## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01:** Single page at `/admin/tenants` with two tabs: "Tenants" and "Signup Requests"
- **D-02:** Tab switching uses client-side state (no navigation) — follows RecipePreview tabbed pattern from Phase 10
- **D-03:** Default tab is "Tenants" (the operational overview)
- **D-04:** Tenant list data table with columns: Name, Status, Subscription Plan, Provisioned (yes/no badge), Applied Recipe, Created Date
- **D-05:** Status displayed as colored badge — green for Active, gray for other states
- **D-06:** Provisioned column: green checkmark for yes, gray dash for no
- **D-07:** Empty state: centered message "No tenants yet — approve signup requests to create tenants"
- **D-08:** Read-only view — no actions on tenants in this phase (management deferred)
- **D-09:** Signup Requests data table with columns: Company Name, Admin Email, Status (badge), Created Date, Actions
- **D-10:** Status filter toggle at top — default shows "Pending" requests, option to show all
- **D-11:** Expandable row to view full request details: Company Name, Admin Email, Phone, Company Size, Address, Billing Contact, Recipe ID, Review Note
- **D-12:** Actions column: "Approve" and "Reject" buttons shown only for Pending requests
- **D-13:** Empty state for no pending: "No pending signup requests"
- **D-14:** Approve action: confirmation modal with optional approval note text field, then auto-triggers provisioning (chains POST /admin/signup/{id}/approve → POST /admin/tenants/{id}/provision)
- **D-15:** Reject action: confirmation modal with required rejection reason text field
- **D-16:** After successful approve+provision or reject, row updates in-place (optimistic UI) and success toast/notification displayed
- **D-17:** If provisioning fails after approval, show error message but signup status remains Approved — admin can retry via a "Provision" button that appears on Approved-but-not-provisioned entries

### Claude's Discretion
- Exact Tailwind styling for table rows, badges, tabs (maintain consistency with Phase 9/10 palette)
- Loading skeleton/spinner approach while API calls complete
- Toast/notification style for success/error messages
- Tab component implementation details (CSS-only vs Blazor component)
- Exact spacing and layout proportions
- Whether expandable row uses animation/transition

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope.

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| TNUI-01 | Admin can view a list of all tenants with name, status, and creation date | GET /admin/tenants endpoint needed; Tenant entity has Name, Status, CreatedAt; RecipeList.razor provides table UI pattern |
| TNUI-02 | Admin can view a pending signup request with details | GET /admin/signup/{id} endpoint needed; SignupRequest entity has all required detail fields; expandable row pattern follows RecipePreview |
| TNUI-03 | Admin can approve a pending signup request (triggers provisioning) | POST /admin/signup/{id}/approve exists; POST /admin/tenants/{id}/provision exists; chaining requires two sequential API calls with error handling |
| TNUI-04 | Admin can reject a pending signup request with a reason | POST /admin/signup/{id}/reject exists; requires confirmation modal with reason field |

## Standard Stack

### Core Technologies
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Razor Server Components | .NET 10.0 | Page structure, event handling, state | Established in Phase 9/10; native to Aspire, no external dependencies |
| Tailwind CSS v4 | 4.0 | Styling (via standalone CLI) | Standardized in Phase 9; configured via MSBuild integration, no npm |
| xUnit | 2.9.3 | Testing framework | Project standard per CLAUDE.md; used for all integration tests |
| EntityFrameworkCore | 10.0.5 | Data access (backend only) | Project standard; Npgsql 10.0.1 for PostgreSQL |

### Supporting UI Patterns (Direct Reuse)
| Component | Source | Purpose | Implementation |
|-----------|--------|---------|-----------------|
| Data table with badges | RecipeList.razor (lines 68-125) | Status badges, action buttons, row hover | Copy structure; replace columns and data source |
| Client-side tabs | RecipePreview.razor (lines 38-51) | Tab switching without navigation | Copy `_activeTab` state + `_tabs` array + button logic |
| Empty state | RecipeList.razor (lines 56-66) | Centered message when no data | Copy structure; update message text |
| Loading state | RecipeList.razor (lines 49-54) | Simple text placeholder | Copy structure |
| Error banner | RecipeList.razor (lines 33-46) | Red alert box for API errors | Copy structure |
| Confirmation modal | Not found in existing code — new pattern | Approve/Reject confirmation with optional text | Design per Claude's discretion; use Tailwind modal classes |
| Expandable row | Not found in existing code — new pattern | Click row to expand and view full details | Design per Claude's discretion; state-driven visibility toggle |

### HTTP Client Pattern
| Item | Configuration | Usage |
|------|---------------|-------|
| Named client | "AdminApi" | Configured in Program.cs with BearerTokenHandler |
| Token injection | BearerTokenHandler | Auto-attaches JWT from ProtectedSessionStorage key "auth_token" |
| Method signature | `HttpClientFactory.CreateClient("AdminApi")` | All API calls in Blazor pages use this |
| Deserialization | `GetFromJsonAsync<T>` / `PostAsJsonAsync` | Auto-parses JSON to strongly-typed DTOs |
| Error handling | Check response.StatusCode; redirect to /login on 401 | Established pattern in RecipeList.razor (lines 196-200) |

### API Pattern (Backend Endpoints)
| Item | Format | Example |
|------|--------|---------|
| Route | MapPost/MapGet under `/admin` group | ApproveTenantEndpoint.Map() (line 12-14) |
| Request/Response | Nested record types | ApproveTenantEndpoint.Request/Response (lines 17-18) |
| Authorization | RequireAuthorization() on group | Endpoints.cs line 78 |
| Return type | `Results<Ok<T>, NotFound, ValidationError>` | ApproveTenantEndpoint line 20 |
| Validation | FluentValidation (implicit in return) | ApproveTenantEndpoint line 33 |

### Installation / Version Verification
No new packages required. Existing dependencies:
```
IronMonkey.Web — already has System.Net.Http.Headers, Microsoft.AspNetCore.Components.Server
IronMonkey.ApiService — already has FluentValidation, EntityFrameworkCore
IronMonkey.Tests — already has xUnit, Testcontainers.PostgreSql
```

## Architecture Patterns

### Page Structure (Established from Phase 9/10)

```
IronMonkey.Web/Components/Pages/Admin/Tenants/TenantManagement.razor
├── @page "/admin/tenants"
├── @attribute [Authorize]
├── @inject IHttpClientFactory HttpClientFactory
├── @inject NavigationManager Nav
│
├── Page heading + empty state message
├── Tab selector (client-side button group)
├── Loading state (simple spinner/text)
├── Error banner (red box)
├── Success banner (green box)
├── Tab 1: Tenants list (data table)
└── Tab 2: Signup Requests (data table with expandable rows)

@code {
  private List<TenantListItem> _tenants = [];
  private List<SignupRequestListItem> _signups = [];
  private string _activeTab = "Tenants";
  private bool _isLoading = true;
  private bool _showAllSignups = false;
  // ... state for modals, approvals, etc.
}
```

### Tab Implementation Pattern (Client-Side Only)

```razor
<!-- Tab selector buttons (no routing, pure @onclick) -->
<div class="border-b border-slate-200 mb-6">
  <nav class="flex space-x-8">
    @foreach (var tab in new[] { "Tenants", "Signup Requests" })
    {
      <button @onclick="() => _activeTab = tab"
              class="@(_activeTab == tab ? "border-b-2 border-indigo-600 text-indigo-600 font-medium" : "text-slate-600") px-1 py-4 text-sm">
        @tab
      </button>
    }
  </nav>
</div>

<!-- Tab content conditional rendering -->
@if (_activeTab == "Tenants") { /* tenants table */ }
else if (_activeTab == "Signup Requests") { /* signup table */ }
```

### Data Table Pattern with Status Badges

```razor
<div class="overflow-x-auto rounded-lg border border-slate-200">
  <table class="w-full">
    <thead class="bg-slate-50 border-b border-slate-200">
      <tr>
        <th class="px-6 py-3 text-left text-xs font-semibold text-slate-900 uppercase tracking-wide">Name</th>
        <th class="px-6 py-3 text-left text-xs font-semibold text-slate-900 uppercase tracking-wide">Status</th>
        <!-- more columns -->
      </tr>
    </thead>
    <tbody>
      @foreach (var item in DisplayedItems)
      {
        <tr class="border-b border-slate-100 hover:bg-slate-50">
          <td class="px-6 py-4 text-sm text-slate-900">@item.Name</td>
          <td class="px-6 py-4">
            <span class="inline-flex items-center rounded-full px-2 py-1 text-xs font-semibold @GetStatusBadgeClass(item.Status)">
              @item.Status
            </span>
          </td>
        </tr>
      }
    </tbody>
  </table>
</div>

@code {
  private static string GetStatusBadgeClass(string status) =>
    status == "Active" ? "bg-green-100 text-green-800" : "bg-slate-100 text-slate-600";
}
```

### Approval Flow (Two-Step Chaining)

**Problem:** Approve button triggers two sequential API calls: POST /admin/signup/{id}/approve, then POST /admin/tenants/{id}/provision. Failure at step 2 must not lose approval state.

**Pattern:**
```csharp
private async Task HandleApproveAsync(Guid signupId)
{
  try {
    // Step 1: Approve signup request
    var approveResponse = await client.PostAsJsonAsync(
      $"/admin/signup/{signupId}/approve",
      new { ApprovalNote = _approvalNote });
    
    if (!approveResponse.IsSuccessStatusCode) throw new Exception("Approval failed");
    
    // Step 2: Provision tenant
    var provisionResponse = await client.PostAsJsonAsync(
      $"/admin/tenants/{signupId}/provision",
      null);
    
    if (provisionResponse.IsSuccessStatusCode) {
      // Update UI optimistically: mark row as Approved + Provisioned
      var item = _signups.First(s => s.Id == signupId);
      item.Status = "Approved";
      item.IsProvisioned = true;
      _successMessage = "Signup approved and tenant provisioned.";
    } else {
      // Step 2 failed but Step 1 succeeded: show "Retry Provision" button
      var item = _signups.First(s => s.Id == signupId);
      item.Status = "Approved";
      item.IsProvisioned = false;
      _errorMessage = "Approval succeeded but provisioning failed. Use 'Provision' button to retry.";
    }
  }
  catch (Exception ex) {
    _errorMessage = $"Error: {ex.Message}";
  }
}
```

### Status Filter Toggle Pattern

```csharp
private List<SignupRequestListItem> DisplayedSignups =>
  _showAllSignups
    ? _signups
    : _signups.Where(s => s.Status == "Pending").ToList();

// Toggle binding
<label>
  <input type="checkbox" @bind="_showAllSignups" />
  Show all signup requests
</label>
```

### Expandable Row Pattern (Not Found; New Design)

**Recommendation:** Use state-driven visibility toggle:

```csharp
private Guid? _expandedRowId = null;

// Toggle expanded state
private void ToggleRowExpand(Guid id) {
  _expandedRowId = _expandedRowId == id ? null : id;
}

// Render expanded detail row if _expandedRowId matches
@foreach (var item in DisplayedSignups) {
  <tr class="border-b border-slate-100 hover:bg-slate-50">
    <!-- regular cells -->
  </tr>
  @if (_expandedRowId == item.Id) {
    <tr class="bg-slate-50 border-b border-slate-100">
      <td colspan="99" class="px-6 py-4">
        <!-- Detail view: all SignupRequest fields -->
        <div class="space-y-2">
          <p><strong>Phone:</strong> @item.Phone</p>
          <p><strong>Company Size:</strong> @item.CompanySize</p>
          <!-- ... etc -->
        </div>
      </td>
    </tr>
  }
}
```

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Tab switching | Custom state machine for routing | Client-side `_activeTab` variable + button @onclick | Simpler, matches Phase 10 pattern, no navigation overhead |
| Status color mapping | Hardcoded color strings in markup | `GetStatusBadgeClass()` helper method | Single source of truth; easier to refactor colors globally |
| HTTP client setup | Manual HttpClient instantiation | `IHttpClientFactory.CreateClient("AdminApi")` | Auto-attaches Bearer token, centralized config |
| Table pagination | Custom offset/limit logic | Start with no pagination; full list in view | TNUI spec doesn't require pagination; add later if list grows |
| Modal dialogs | CSS-only Tailwind modals from scratch | Use established modal pattern (build once, reuse) | Modals for Approve/Reject confirmation — build a reusable component or inline markup pattern |
| Form validation | Manual string checks | FluentValidation on backend, DataAnnotationsValidator on frontend | Rejection reason is required; approval note optional — validate on submit |

**Key insight:** RecipeList.razor and RecipePreview.razor exist because Phase 10 solved these problems. Copy their patterns verbatim; don't reimplement. The only new patterns are expandable rows and confirmation modals, which are simple Tailwind + state combinations.

## Common Pitfalls

### Pitfall 1: Forgot to Create GET /admin/tenants Endpoint
**What goes wrong:** Code tries to call `GET /admin/tenants` to fetch tenant list for TNUI-01, but endpoint doesn't exist (only `/admin/signup` exists for signup requests). Page shows empty state or 404 error.

**Why it happens:** CONTEXT.md notes "Gap: No list tenants endpoint exists" — the backend team built approval/provision/list-signups but not list-tenants.

**How to avoid:** Phase plan MUST include a new `ListTenantsEndpoint.cs` that queries CentralDbContext.Tenants and returns TenantSummary records (Id, Name, Status, SubscriptionPlan, IsProvisioned, AppliedRecipeId, CreatedAt). Follow the ApproveTenantEndpoint pattern exactly.

**Warning signs:** UI loads successfully but Tenants tab shows "No tenants yet" even after tenants were approved + provisioned in previous phases.

### Pitfall 2: Forgot to Create GET /admin/signup/{id} Endpoint
**What goes wrong:** ListSignupRequestsEndpoint returns only summary data (Id, CompanyName, AdminEmail, Status, CreatedAt), but the expandable detail row needs Phone, CompanySize, Address, BillingContact, RecipeId, ReviewNote. UI code must call a detail endpoint that doesn't exist.

**Why it happens:** ListSignupRequestsEndpoint was designed for list-only use case; details were assumed to be cached on client after expansion. But new detail view requires fetching full SignupRequest record.

**How to avoid:** Either (a) enhance ListSignupRequestsEndpoint to return full SignupRequest data in one call (simplest if list is not huge), or (b) add new `GetSignupRequestEndpoint` for detail fetch (better if list grows large). Recommend option (b) to match REST conventions.

**Warning signs:** Expandable row shows only summary fields; clicking "Expand" renders null/empty for detail fields.

### Pitfall 3: Approval Chaining Loses State on Partial Failure
**What goes wrong:** User clicks Approve → POST /admin/signup/{id}/approve succeeds → POST /admin/tenants/{id}/provision fails → UI shows error "Provisioning failed" → user refreshes page → signup status is "Approved" in DB but row shows as "Pending" in UI because page re-fetches list.

**Why it happens:** CONTEXT.md decision D-17 explicitly handles this: "If provisioning fails after approval, show error message but signup status remains Approved — admin can retry via a Provision button." Implementer must account for the Approved-but-not-provisioned state and show a retry button.

**How to avoid:** After both API calls complete, check response codes separately. If approve succeeds but provision fails, update UI to show Approved status + "Retry Provision" action button. If both succeed, update to Provisioned. Never assume both succeed or both fail.

**Warning signs:** Clicking "Approve" on a signup triggers provisioning; if provisioning fails, row disappears from pending list (user thinks it's gone when it's actually stuck in Approved state).

### Pitfall 4: Missing Isloading State During Long API Calls
**What goes wrong:** User clicks "Approve" button → HTTP call takes 3+ seconds (provisioning is slow) → user sees no visual feedback → clicks button again → duplicate provisioning attempts trigger.

**Why it happens:** Forgot to set `_isApproving = true` before API call and `_isApproving = false` after response.

**How to avoid:** Wrap all long API calls in a guard flag. Set flag true before call, false after response (in finally block). Disable the button while flag is true. See RecipeList.razor lines 181-215 for exact pattern.

**Warning signs:** Clicking action buttons multiple times triggers multiple API requests visible in network tab; backend provisioning service complains about duplicate attempts.

### Pitfall 5: Expandable Row State Bleeds Across Pagination or Re-renders
**What goes wrong:** User expands row #1 → scrolls down → re-renders table → row #3 is now expanded instead (because expanded state is based on index instead of ID).

**Why it happens:** Used `_expandedRowIndex` instead of `_expandedRowId` to track which row is open.

**How to avoid:** Always key expanded/active state by unique ID (Guid), never by index or name. Use `_expandedRowId = null` or `_expandedRowId == item.Id` comparisons.

**Warning signs:** Scroll or filter the list; expanded row jumps to a different item.

## Code Examples

### Backend: GET /admin/tenants Endpoint (Must Create)

```csharp
// Source: Modeled after ListSignupRequestsEndpoint and ApproveTenantEndpoint patterns
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class ListTenantsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/tenants", Handle)
        .WithSummary("List all provisioned tenants")
        .WithTags("Platform Admin");

    public record TenantSummary(
        Guid Id,
        string Name,
        string Status,
        string SubscriptionPlan,
        bool IsProvisioned,
        Guid? AppliedRecipeId,
        DateTime CreatedAt);

    private static async Task<Ok<List<TenantSummary>>> Handle(
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var results = await centralDb.Tenants
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TenantSummary(
                t.Id,
                t.Name,
                t.Status,
                t.SubscriptionPlan,
                t.IsProvisioned,
                t.AppliedRecipeId,
                t.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(results);
    }
}
```

### Backend: GET /admin/signup/{id} Endpoint (Must Create)

```csharp
// Source: Modeled after ApproveTenantEndpoint pattern for detail retrieval
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class GetSignupRequestEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/signup/{id}", Handle)
        .WithSummary("Get full details of a signup request")
        .WithTags("Platform Admin");

    public record SignupRequestDetail(
        Guid Id,
        string CompanyName,
        string AdminEmail,
        string Phone,
        string CompanySize,
        string Address,
        string BillingContact,
        Guid? RecipeId,
        string Status,
        string? ReviewNote,
        DateTime CreatedAt);

    private static async Task<Results<Ok<SignupRequestDetail>, NotFound>> Handle(
        Guid id,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var signup = await centralDb.SignupRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (signup is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(new SignupRequestDetail(
            signup.Id,
            signup.CompanyName,
            signup.AdminEmail,
            signup.Phone,
            signup.CompanySize,
            signup.Address,
            signup.BillingContact,
            signup.RecipeId,
            signup.Status,
            signup.ReviewNote,
            signup.CreatedAt));
    }
}
```

### Frontend: TenantManagement.razor Page Structure

```razor
@page "/admin/tenants"
@attribute [Authorize]
@using IronMonkey.Data.Entities
@inject IHttpClientFactory HttpClientFactory
@inject NavigationManager Nav

<PageTitle>Tenant Management — IronMonkey Admin</PageTitle>

<div class="space-y-6">
  <!-- Title -->
  <h1 class="text-2xl font-bold text-slate-900">Tenant Management</h1>

  <!-- Tab selector -->
  <div class="border-b border-slate-200">
    <nav class="flex space-x-8">
      @foreach (var tab in new[] { "Tenants", "Signup Requests" })
      {
        <button type="button" @onclick="() => _activeTab = tab"
                class="@(_activeTab == tab
                  ? "border-b-2 border-indigo-600 text-indigo-600 font-medium"
                  : "text-slate-600 hover:text-slate-900") px-1 py-4 text-sm transition-colors">
          @tab
        </button>
      }
    </nav>
  </div>

  <!-- Alerts -->
  @if (!string.IsNullOrEmpty(_errorMessage))
  {
    <div class="rounded-lg bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
      @_errorMessage
    </div>
  }

  @if (!string.IsNullOrEmpty(_successMessage))
  {
    <div class="rounded-lg bg-green-50 border border-green-200 px-4 py-3 text-sm text-green-700">
      @_successMessage
    </div>
  }

  <!-- Loading state -->
  @if (_isLoading)
  {
    <div class="py-12 text-center">
      <p class="text-sm text-slate-500">Loading...</p>
    </div>
  }

  <!-- Tab content: Tenants -->
  @if (_activeTab == "Tenants" && !_isLoading)
  {
    @if (_tenants.Count == 0)
    {
      <div class="rounded-xl border border-dashed border-slate-300 bg-slate-50 py-16 text-center">
        <p class="text-slate-600">No tenants yet — approve signup requests to create tenants</p>
      </div>
    }
    else
    {
      <!-- Tenants table -->
    }
  }

  <!-- Tab content: Signup Requests -->
  @if (_activeTab == "Signup Requests" && !_isLoading)
  {
    <!-- Show all filter -->
    <div class="flex items-center gap-2 mb-4">
      <input type="checkbox" id="showAllSignups" @bind="_showAllSignups" />
      <label for="showAllSignups" class="text-sm font-medium text-slate-700">Show all requests</label>
    </div>

    @if (DisplayedSignups.Count == 0)
    {
      <div class="rounded-xl border border-dashed border-slate-300 bg-slate-50 py-16 text-center">
        <p class="text-slate-600">@(_showAllSignups ? "No signup requests." : "No pending signup requests")</p>
      </div>
    }
    else
    {
      <!-- Signups table with expandable rows -->
    }
  }
</div>

@code {
  private string _activeTab = "Tenants";
  private List<TenantListItem> _tenants = [];
  private List<SignupRequestListItem> _signups = [];
  private bool _isLoading = true;
  private bool _showAllSignups = false;
  private Guid? _expandedRowId = null;
  private string? _errorMessage = null;
  private string? _successMessage = null;
  private bool _isApproving = false;
  private Guid? _approvingId = null;

  private List<SignupRequestListItem> DisplayedSignups =>
    _showAllSignups ? _signups : _signups.Where(s => s.Status == "Pending").ToList();

  protected override async Task OnInitializedAsync()
  {
    await LoadDataAsync();
  }

  private async Task LoadDataAsync()
  {
    _isLoading = true;
    _errorMessage = null;
    try
    {
      var client = HttpClientFactory.CreateClient("AdminApi");
      _tenants = await client.GetFromJsonAsync<List<TenantListItem>>("/admin/tenants") ?? [];
      _signups = await client.GetFromJsonAsync<List<SignupRequestListItem>>("/admin/signup") ?? [];
    }
    catch (Exception ex)
    {
      _errorMessage = $"Failed to load data: {ex.Message}";
    }
    finally
    {
      _isLoading = false;
    }
  }

  private void ToggleRowExpand(Guid id)
  {
    _expandedRowId = _expandedRowId == id ? null : id;
  }

  private async Task HandleApproveAsync(Guid signupId)
  {
    _isApproving = true;
    _approvingId = signupId;
    _errorMessage = null;
    _successMessage = null;

    try
    {
      var client = HttpClientFactory.CreateClient("AdminApi");

      // Step 1: Approve signup
      var approveResponse = await client.PostAsJsonAsync(
        $"/admin/signup/{signupId}/approve",
        new { ApprovalNote = "" });

      if (!approveResponse.IsSuccessStatusCode)
      {
        _errorMessage = "Failed to approve signup request.";
        return;
      }

      // Step 2: Provision tenant
      var provisionResponse = await client.PostAsJsonAsync(
        $"/admin/tenants/{signupId}/provision",
        null);

      if (provisionResponse.IsSuccessStatusCode)
      {
        // Both succeeded
        var item = _signups.FirstOrDefault(s => s.Id == signupId);
        if (item is not null)
        {
          item.Status = "Approved";
          item.IsProvisioned = true;
        }
        _successMessage = "Signup approved and tenant provisioned successfully.";
        await LoadDataAsync(); // Refresh list
      }
      else
      {
        // Approve succeeded, provision failed
        var item = _signups.FirstOrDefault(s => s.Id == signupId);
        if (item is not null)
        {
          item.Status = "Approved";
          item.IsProvisioned = false;
        }
        _errorMessage = "Approval succeeded but provisioning failed. Use 'Provision' button to retry.";
      }
    }
    catch (Exception ex)
    {
      _errorMessage = $"Error: {ex.Message}";
    }
    finally
    {
      _isApproving = false;
      _approvingId = null;
    }
  }

  private async Task HandleRejectAsync(Guid signupId, string reason)
  {
    _errorMessage = null;
    _successMessage = null;

    try
    {
      var client = HttpClientFactory.CreateClient("AdminApi");
      var response = await client.PostAsJsonAsync(
        $"/admin/signup/{signupId}/reject",
        new { RejectionReason = reason });

      if (response.IsSuccessStatusCode)
      {
        var item = _signups.FirstOrDefault(s => s.Id == signupId);
        if (item is not null) item.Status = "Rejected";
        _successMessage = "Signup request rejected.";
      }
      else
      {
        _errorMessage = "Failed to reject signup request.";
      }
    }
    catch (Exception ex)
    {
      _errorMessage = $"Error: {ex.Message}";
    }
  }

  private class TenantListItem
  {
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string SubscriptionPlan { get; set; } = string.Empty;
    public bool IsProvisioned { get; set; }
    public Guid? AppliedRecipeId { get; set; }
    public DateTime CreatedAt { get; set; }
  }

  private class SignupRequestListItem
  {
    public Guid Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string AdminEmail { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string CompanySize { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string BillingContact { get; set; } = string.Empty;
    public Guid? RecipeId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsProvisioned { get; set; }
  }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Full-page navigation for tabs | Client-side state machine with `_activeTab` variable | Phase 10 (RecipePreview.razor) | Faster UX, no server round-trip, simpler code |
| Hardcoded color strings in markup | `GetStatusBadgeClass()` helper method | Phase 10 (RecipeList.razor) | Single source of truth for colors, easier theming |
| Separate pages per resource | Single component with conditional rendering + tabs | Phase 10 (RecipePreview.razor) | Less overhead, unified layout, simpler navigation |

**Deprecated/outdated:**
- Inline expansion logic in foreach loop — use state-driven `_expandedRowId` instead
- Modal HTML written inline each time — extract to reusable pattern or component (Claude's discretion)

## Open Questions

1. **Should GET /admin/tenants return full Tenant entities or summaries?**
   - What we know: ListSignupRequestsEndpoint returns summaries; ApproveTenantEndpoint doesn't query Tenant list
   - What's unclear: Whether UI needs AppliedRecipeId/Version or just status + name
   - Recommendation: Return full TenantSummary with all columns needed for D-04 table (Name, Status, SubscriptionPlan, IsProvisioned, AppliedRecipeId, CreatedAt). Simpler than fetching separately.

2. **Should SignupRequest details endpoint be in the list response or a separate GET?**
   - What we know: ListSignupRequestsEndpoint currently returns only 5 fields; expandable row needs 11+ fields
   - What's unclear: Whether to merge detail into list response or add separate GET /admin/signup/{id}
   - Recommendation: Add separate GET /admin/signup/{id} endpoint (cleaner API, matches REST conventions). UI fetches list once, then fetches detail on expand.

3. **What if tenant provisioning takes > 30 seconds?**
   - What we know: ProvisionTenantEndpoint spawns async work but doesn't support long-polling
   - What's unclear: Should phase include SignalR update or simple "check back later"?
   - Recommendation: Keep simple for Phase 11; show "Provisioning..." spinner and disable button. SignalR updates deferred to later phases.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 10.0 runtime | Build and runtime | ✓ | 10.0.2 (verified in IronMonkey.Tests.csproj) | — |
| PostgreSQL | Backend tests (ListTenantsEndpoint integration tests) | ✓ | 15-alpine (via Testcontainers) | In-memory EF Core for unit tests |
| Docker | Testcontainers for integration tests | ✓ | Required for full test suite | Mock CentralDbContext for local unit testing |

No external UI dependencies (Node.js, npm). Tailwind compiles via standalone CLI with MSBuild integration (already configured in Phase 9).

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 + Testcontainers.PostgreSql 4.3.0 |
| Config file | IronMonkey.Tests.csproj (lines 1-27) |
| Quick run command | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ListTenants" -x` |
| Full suite command | `dotnet test IronMonkey.Tests` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| TNUI-01 | Admin can view list of all tenants | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ListTenantsTests"` | ❌ Wave 0 |
| TNUI-02 | Admin can view signup request details | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~GetSignupRequestTests"` | ❌ Wave 0 |
| TNUI-03 | Admin can approve signup (chains approve + provision) | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ApproveAndProvisionTests"` | ❌ Wave 0 |
| TNUI-04 | Admin can reject signup with reason | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~RejectSignupTests"` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ListTenants" -x` (single endpoint)
- **Per wave merge:** `dotnet test IronMonkey.Tests` (all tests including cross-tenant security, provisioning)
- **Phase gate:** Full suite green + manual UI smoke test (click tabs, expand rows, submit approve/reject)

### Wave 0 Gaps
- [ ] `IronMonkey.Tests/Integration/ListTenantsTests.cs` — covers TNUI-01 (GET /admin/tenants returns tenant list with correct columns)
- [ ] `IronMonkey.Tests/Integration/GetSignupRequestTests.cs` — covers TNUI-02 (GET /admin/signup/{id} returns full detail)
- [ ] `IronMonkey.Tests/Integration/ApproveAndProvisionTests.cs` — covers TNUI-03 (two-step chaining, failure handling)
- [ ] `IronMonkey.Tests/Integration/RejectSignupTests.cs` — covers TNUI-04 (reject updates status)

*(Tests follow PostgreSqlFixture pattern from existing TenantProvisioningTests.cs)*

## Sources

### Primary (HIGH confidence)
- **Project CLAUDE.md** — Confirmed .NET 10.0, Aspire, xUnit 2.9.3, Testcontainers, no Node.js for Tailwind
- **CONTEXT.md (11-CONTEXT.md)** — Locked decisions D-01 through D-17, backend endpoint specs, reusable patterns
- **REQUIREMENTS.md** — TNUI-01 through TNUI-04 requirement specs, traceability matrix
- **RecipeList.razor** (actual code) — Data table, status badges, empty state, loading state, error banner, action buttons
- **RecipePreview.razor** (actual code) — Client-side tab switching, tab state management
- **ApproveTenantEndpoint.cs, RejectTenantEndpoint.cs, ProvisionTenantEndpoint.cs** (actual code) — Minimal API pattern, request/response DTOs, authorization
- **ListSignupRequestsEndpoint.cs** (actual code) — GET endpoint pattern, query filtering, OrderByDescending
- **Tenant.cs, SignupRequest.cs** (actual code) — Entity properties, factory methods, state transition methods (Approve, Reject)
- **BearerTokenHandler.cs** (actual code) — HTTP client token injection, error handling
- **AdminSidebar.razor** (actual code) — Route structure already includes `/admin/tenants` and `/admin/signups` navigation links

### Secondary (MEDIUM confidence)
- **STATE.md** — Phase 10 completion, test infrastructure confirmed, STATE.md mentions "Dual API fetch on RecipeEdit" suggesting pattern reuse is standard

### Tertiary (LOW confidence)
- None — all findings cross-verified with actual codebase

## Metadata

**Confidence breakdown:**
- **Standard stack:** HIGH — Verified with actual source code (CLAUDE.md, .csproj files, Program.cs, existing components)
- **Architecture patterns:** HIGH — RecipeList.razor and RecipePreview.razor are proven patterns in codebase, adapted directly
- **API endpoints:** HIGH — All backend endpoints (ApproveTenantEndpoint, RejectTenantEndpoint, ProvisionTenantEndpoint) examined; gaps (ListTenants, GetSignupRequest detail) clearly identified
- **Pitfalls:** HIGH — CONTEXT.md explicitly flags these (D-14 chaining, D-17 partial failure handling)
- **Testing:** HIGH — xUnit setup verified with csproj and existing integration tests (TenantProvisioningTests.cs pattern clear)

**Research date:** 2026-04-01
**Valid until:** 2026-04-08 (Blazor, Tailwind, xUnit stable; patterns locked in CONTEXT.md)
