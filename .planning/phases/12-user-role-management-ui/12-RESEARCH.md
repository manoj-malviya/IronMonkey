# Phase 12: User & Role Management UI - Research

**Researched:** 2026-04-01
**Domain:** Blazor Server UI + ASP.NET Minimal API for tenant-scoped user and role management
**Confidence:** HIGH

## Summary

Phase 12 extends the admin UI with user account management — list, create, edit, and deactivate users within a tenant. The phase builds directly on Phase 9 (auth foundation) and Phase 10/11 (UI patterns). All frontend pages follow existing RecipeList/RecipeCreate/RecipeEdit Razor patterns. Backend requires new endpoints: `ListUsers`, `GetUser`, `UpdateUser`, `DeactivateUser`, and `ResetPassword`. Password generation and hashing use BCrypt (already in LoginEndpoint).

The critical constraint: existing `CreateUser` endpoint stores passwords plaintext and uses `AppDbContext` (central DB). Phase 12 must either fix CreateUser to hash with BCrypt and scope to tenant DB, or create a new tenant-scoped variant. All new user-management endpoints must extract `tenant_id` from JWT claims via `IUserContext`.

**Primary recommendation:** Create new tenant-scoped endpoints at `/api/user-management/users/*` using `TenantDbContext`, extract tenant from JWT via `IUserContext`, hash passwords with BCrypt.Net, and follow the established Razor form + modal patterns from Phases 10/11.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01–D-06:** UserList page at `/admin/users` with status badges, deactivation toggle, empty state — mirrors RecipeList pattern
- **D-07–D-12:** CreateUser page at `/admin/users/create` with auto-generated password (displayed once), inline validation
- **D-13–D-17:** EditUser page at `/admin/users/{id}/edit` with separate "Reset Password" button
- **D-18–D-21:** Deactivate via confirmation modal with optimistic UI update (IsDeleted soft delete)
- **Backend:** CreateUser must hash password with BCrypt before saving (currently plaintext), endpoint uses TenantDbContext not AppDbContext, role dropdown from ListRoles endpoint

### Claude's Discretion
- Exact Tailwind styling for table/badges/forms (maintain Phase 9/10 palette)
- Loading skeleton/spinner approach
- Toast/notification style for success messages
- Password display UX (modal vs banner, copy button)
- Role dropdown styling and empty-state handling

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| USUI-01 | Admin can view list of users in current tenant (name, email, role, status) | UserList page pattern established; ListUsers endpoint required (endpoint research in Backend API Changes section) |
| USUI-02 | Admin can create new user (name, email, role) with auto-generated password | CreateUser page pattern + password generation strategy documented; BCrypt hashing required in backend |
| USUI-03 | Admin can edit user details and role; ResetPassword endpoint generates new password | EditUser page pattern + GetUser endpoint required; ResetPassword endpoint with BCrypt hashing |
| USUI-04 | Admin can deactivate user (soft delete); status changes in list | Deactivate via DELETE endpoint using IsDeleted; optimistic UI in RecipeList pattern applies |
</phase_requirements>

## Standard Stack

### Core Frontend
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Blazor Server | .NET 10.0 | Interactive UI framework, pre-installed | IronMonkey foundation; server-side rendering + interactivity |
| Razor Components | 10.0.2 | Page/component markup | Native to Blazor Server; RecipeList/Create/Edit patterns already proven |
| Tailwind CSS | v4 (standalone CLI) | Utility-first CSS | Phase 9 established; MSBuild-integrated (no Node.js dependency) |
| Heroicons | (inline SVG) | Icon assets | Already used in RecipeList, AdminSidebar, TenantManagement |
| DataAnnotations | .NET 10.0 | Client-side form validation | RecipeCreate uses `DataAnnotationsValidator` in EditForm |

### Core Backend
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| ASP.NET Minimal API | 10.0 | Endpoint definitions | IronMonkey standard; LoginEndpoint, RecipeEndpoints, TenantEndpoints follow this pattern |
| Entity Framework Core | 10.0.5 | ORM + database access | Central and tenant DB contexts; TenantDbContext required for user-scoped data |
| BCrypt.Net | (version TBD) | Password hashing | LoginEndpoint uses BCrypt.Verify; CreateUser currently missing hashing |
| FluentValidation | (in use) | Request validation | CreateUser.RequestValidator pattern; required for new endpoints |

### HTTP & Authentication
| Component | Purpose | Pattern |
|-----------|---------|---------|
| `BearerTokenHandler` | Auto-injects JWT from session storage | Proven in RecipeList/Create HTTP calls |
| `IUserContext` | Extracts `tenant_id`, `user_id` from JWT claims | ClaimsPrincipalExtensions parses "tenant_id" claim, used by LoginEndpoint |
| `HttpClientFactory.CreateClient("AdminApi")` | Named HttpClient with bearer token | All admin pages use this for API calls |

### Supporting Libraries (Existing)
| Library | Purpose | When Used |
|---------|---------|-----------|
| System.ComponentModel.DataAnnotations | Validation attributes on form models | RecipeCreate form model validation |
| System.Security.Claims | JWT claim parsing | UserContext extracts tenant_id from claims |
| Microsoft.AspNetCore.Components.Authorization | AuthenticationStateProvider cascade | AdminSidebar reads user role from claims |

## Architecture Patterns

### Recommended Frontend Structure
```
IronMonkey.Web/Components/Pages/Admin/Users/
├── UserList.razor              # List all users in tenant (USUI-01)
├── UserCreate.razor            # Form to create new user (USUI-02)
├── UserEdit.razor              # Form to edit user + reset password button (USUI-03)
└── Shared/
    └── UserPasswordDisplay.razor # Reusable component for showing/copying password
```

### Pattern 1: UserList Page (USUI-01)
**What:** Data table showing all users in current tenant with name, email, role, status (Active/Deactivated), created date. Status as colored badge. Toggle to show deactivated users. Action buttons: Edit (always), Deactivate (active only).

**When to use:** Whenever listing tenant-scoped entities with filter + action buttons.

**Example:**
```csharp
// UserList.razor
@page "/admin/users"
@attribute [Authorize]
@inject IHttpClientFactory HttpClientFactory
@inject NavigationManager Nav

<PageTitle>User Management — IronMonkey Admin</PageTitle>

<div class="space-y-6">
    <div class="space-y-3">
        <div class="flex items-center justify-between">
            <h1 class="text-2xl font-bold text-slate-900">User Management</h1>
            <button @onclick="NavigateToCreate"
                    class="inline-flex items-center gap-2 rounded-lg bg-indigo-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500">
                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-4 h-4">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
                </svg>
                Create User
            </button>
        </div>
        <div class="flex items-center gap-2">
            <input type="checkbox" id="showInactive" @bind="_showInactive" class="rounded border-slate-300" />
            <label for="showInactive" class="text-sm font-medium text-slate-700">Show Deactivated Users</label>
        </div>
    </div>

    @if (!_isLoading && DisplayedUsers.Count == 0)
    {
        <div class="rounded-xl border border-dashed border-slate-300 bg-slate-50 py-16 text-center">
            <p class="text-slate-600">No users found.</p>
            <button @onclick="NavigateToCreate"
                    class="mt-4 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-500">
                Create your first user
            </button>
        </div>
    }

    @if (!_isLoading && DisplayedUsers.Count > 0)
    {
        <div class="overflow-x-auto rounded-lg border border-slate-200">
            <table class="w-full">
                <thead class="bg-slate-50 border-b border-slate-200">
                    <tr>
                        <th class="px-6 py-3 text-left text-xs font-semibold text-slate-900 uppercase tracking-wide">Name</th>
                        <th class="px-6 py-3 text-left text-xs font-semibold text-slate-900 uppercase tracking-wide">Email</th>
                        <th class="px-6 py-3 text-left text-xs font-semibold text-slate-900 uppercase tracking-wide">Role</th>
                        <th class="px-6 py-3 text-left text-xs font-semibold text-slate-900 uppercase tracking-wide">Status</th>
                        <th class="px-6 py-3 text-left text-xs font-semibold text-slate-900 uppercase tracking-wide">Created</th>
                        <th class="px-6 py-3 text-right text-xs font-semibold text-slate-900 uppercase tracking-wide">Actions</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var user in DisplayedUsers)
                    {
                        <tr class="border-b border-slate-100 hover:bg-slate-50">
                            <td class="px-6 py-4 text-sm text-slate-900">@user.Name</td>
                            <td class="px-6 py-4 text-sm text-slate-600">@user.Email</td>
                            <td class="px-6 py-4 text-sm text-slate-600">@user.RoleName</td>
                            <td class="px-6 py-4">
                                <span class="inline-flex items-center rounded-full px-2 py-1 text-xs font-semibold @GetStatusBadgeClass(user.IsActive)">
                                    @(user.IsActive ? "Active" : "Deactivated")
                                </span>
                            </td>
                            <td class="px-6 py-4 text-sm text-slate-500">@user.CreatedAt.ToString("MMM d, yyyy")</td>
                            <td class="px-6 py-4 text-right space-x-2">
                                <button @onclick="() => NavigateToEdit(user.Id)"
                                        class="text-sm font-medium text-indigo-600 hover:text-indigo-700">
                                    Edit
                                </button>
                                @if (user.IsActive)
                                {
                                    <button @onclick="() => HandleDeactivateAsync(user.Id)"
                                            disabled="@(_isDeactivating && _deactivatingId == user.Id)"
                                            class="text-sm font-medium text-red-600 hover:text-red-700 disabled:opacity-50">
                                        @(_isDeactivating && _deactivatingId == user.Id ? "..." : "Deactivate")
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
    private List<UserListItem> _users = [];
    private bool _isLoading = true;
    private bool _showInactive = false;
    private bool _isDeactivating = false;
    private Guid? _deactivatingId = null;
    private string? _errorBanner = null;

    private List<UserListItem> DisplayedUsers =>
        _showInactive ? _users : _users.Where(u => u.IsActive).ToList();

    protected override async Task OnInitializedAsync()
    {
        await LoadUsersAsync();
    }

    private void NavigateToCreate() => Nav.NavigateTo("/admin/users/create");

    private void NavigateToEdit(Guid id) => Nav.NavigateTo($"/admin/users/{id}/edit");

    private async Task LoadUsersAsync()
    {
        _isLoading = true;
        _errorBanner = null;
        try
        {
            var client = HttpClientFactory.CreateClient("AdminApi");
            var list = await client.GetFromJsonAsync<List<UserListItem>>("/api/user-management/users");
            _users = list ?? [];
        }
        catch (Exception ex)
        {
            _errorBanner = $"Failed to load users: {ex.Message}";
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
        try
        {
            var client = HttpClientFactory.CreateClient("AdminApi");
            var response = await client.DeleteAsync($"/api/user-management/users/{id}");
            if (response.IsSuccessStatusCode)
            {
                var item = _users.FirstOrDefault(u => u.Id == id);
                if (item is not null) item.IsActive = false;
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Nav.NavigateTo("/login");
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

    private static string GetStatusBadgeClass(bool isActive) =>
        isActive ? "bg-green-100 text-green-800" : "bg-slate-100 text-slate-600";

    private class UserListItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
```

### Pattern 2: CreateUser Page (USUI-02)
**What:** Form with Name, Email, Role dropdown. No password field — admin doesn't enter password. On successful creation, display generated password once in modal/banner with copy-to-clipboard button. Password auto-generated on server side (12+ chars, mixed case, digits, special chars), hashed with BCrypt for storage.

**Example:**
```csharp
// UserCreate.razor — simplified structure
@page "/admin/users/create"
@attribute [Authorize]
@inject IHttpClientFactory HttpClientFactory
@inject NavigationManager Nav

<h1>Create User</h1>

@if (!string.IsNullOrEmpty(_generatedPassword))
{
    <!-- Password display modal/banner with copy button -->
    <div class="rounded-lg bg-green-50 border border-green-200 px-4 py-3 text-sm">
        <p class="font-medium text-green-800">User created successfully!</p>
        <p class="text-green-700 mt-2">Share this password (displayed once):</p>
        <div class="mt-2 flex items-center gap-2">
            <code class="px-3 py-2 bg-white border border-green-200 rounded font-mono text-sm">@_generatedPassword</code>
            <button @onclick="CopyToClipboard" class="px-3 py-2 bg-green-600 text-white rounded text-sm">Copy</button>
        </div>
    </div>
}
else
{
    <EditForm Model="_model" OnValidSubmit="HandleSaveAsync">
        <DataAnnotationsValidator />
        
        <!-- Name field -->
        <div class="mb-4">
            <label class="block text-sm font-medium text-slate-700">Name</label>
            <InputText @bind-Value="_model.Name" class="w-full rounded-lg border border-slate-300 px-3 py-2" />
            <ValidationMessage For="() => _model.Name" class="text-xs text-red-600" />
        </div>

        <!-- Email field -->
        <div class="mb-4">
            <label class="block text-sm font-medium text-slate-700">Email</label>
            <InputText @bind-Value="_model.Email" class="w-full rounded-lg border border-slate-300 px-3 py-2" />
            <ValidationMessage For="() => _model.Email" class="text-xs text-red-600" />
        </div>

        <!-- Role dropdown — populated from /api/user-management/roles -->
        <div class="mb-4">
            <label class="block text-sm font-medium text-slate-700">Role</label>
            <select @bind="_selectedRoleId" class="w-full rounded-lg border border-slate-300 px-3 py-2">
                <option value="">Select a role</option>
                @foreach (var role in _roles)
                {
                    <option value="@role.Id">@role.Name</option>
                }
            </select>
            @if (string.IsNullOrEmpty(_selectedRoleId))
            {
                <p class="mt-1 text-xs text-red-600">Role is required</p>
            }
        </div>

        <button type="submit" disabled="@_isLoading" class="rounded-lg bg-indigo-600 px-6 py-2.5 text-sm font-semibold text-white">
            @(_isLoading ? "Creating..." : "Create User")
        </button>
    </EditForm>
}

@code {
    private UserFormModel _model = new();
    private List<RoleOption> _roles = [];
    private string? _selectedRoleId = null;
    private string? _generatedPassword = null;
    private bool _isLoading = false;

    protected override async Task OnInitializedAsync()
    {
        await LoadRolesAsync();
    }

    private async Task LoadRolesAsync()
    {
        try
        {
            var client = HttpClientFactory.CreateClient("AdminApi");
            var roles = await client.GetFromJsonAsync<List<RoleOption>>("/api/user-management/roles");
            _roles = roles ?? [];
        }
        catch { }
    }

    private async Task HandleSaveAsync()
    {
        _isLoading = true;
        try
        {
            var client = HttpClientFactory.CreateClient("AdminApi");
            var payload = new { _model.Name, _model.Email, RoleId = int.Parse(_selectedRoleId ?? "0") };
            var response = await client.PostAsJsonAsync("/api/user-management/users", payload);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<CreateUserResponse>();
                _generatedPassword = result?.GeneratedPassword;
                // After user closes banner, navigate back to list
            }
        }
        catch { }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task CopyToClipboard()
    {
        // Use JS interop or native browser API to copy _generatedPassword
        await Task.Delay(100); // Placeholder
    }

    private record CreateUserResponse(Guid UserId, string GeneratedPassword);
    
    private class UserFormModel
    {
        [Required]
        public string Name { get; set; } = string.Empty;
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    private record RoleOption(int Id, string Name);
}
```

### Pattern 3: EditUser Page (USUI-03)
**What:** Pre-populated form for Name, Email, Role. No password field in main form. Separate "Reset Password" button generates and displays new password (same UX as create). Inline validation + error banner.

**Example:**
```csharp
// UserEdit.razor — simplified
@page "/admin/users/{Id:guid}/edit"
@attribute [Authorize]
@inject IHttpClientFactory HttpClientFactory
@inject NavigationManager Nav

<h1>Edit User: @_user?.Name</h1>

@if (_user != null)
{
    <EditForm Model="_user" OnValidSubmit="HandleSaveAsync">
        <DataAnnotationsValidator />
        
        <!-- Name -->
        <div class="mb-4">
            <label class="block text-sm font-medium text-slate-700">Name</label>
            <InputText @bind-Value="_user.Name" class="w-full rounded-lg border border-slate-300 px-3 py-2" />
            <ValidationMessage For="() => _user.Name" class="text-xs text-red-600" />
        </div>

        <!-- Email -->
        <div class="mb-4">
            <label class="block text-sm font-medium text-slate-700">Email</label>
            <InputText @bind-Value="_user.Email" class="w-full rounded-lg border border-slate-300 px-3 py-2" />
            <ValidationMessage For="() => _user.Email" class="text-xs text-red-600" />
        </div>

        <!-- Role dropdown -->
        <div class="mb-6">
            <label class="block text-sm font-medium text-slate-700">Role</label>
            <select @bind="_selectedRoleId" class="w-full rounded-lg border border-slate-300 px-3 py-2">
                @foreach (var role in _roles)
                {
                    <option value="@role.Id" selected="@(_selectedRoleId == role.Id.ToString())">@role.Name</option>
                }
            </select>
        </div>

        <!-- Reset Password button (separate from form) -->
        <button type="button" @onclick="HandleResetPasswordAsync" disabled="@_isResettingPassword"
                class="mb-4 rounded-lg border border-slate-300 px-4 py-2 text-sm font-semibold text-slate-700 hover:bg-slate-50">
            @(_isResettingPassword ? "Resetting..." : "Reset Password")
        </button>

        <!-- Save button -->
        <button type="submit" disabled="@_isSaving" class="rounded-lg bg-indigo-600 px-6 py-2.5 text-sm font-semibold text-white">
            @(_isSaving ? "Saving..." : "Save Changes")
        </button>
    </EditForm>
}

@code {
    [Parameter] public Guid Id { get; set; }

    private UserEditModel? _user = null;
    private List<RoleOption> _roles = [];
    private string? _selectedRoleId = null;
    private bool _isSaving = false;
    private bool _isResettingPassword = false;

    protected override async Task OnInitializedAsync()
    {
        await LoadUserAsync();
        await LoadRolesAsync();
    }

    private async Task LoadUserAsync()
    {
        try
        {
            var client = HttpClientFactory.CreateClient("AdminApi");
            var user = await client.GetFromJsonAsync<UserEditModel>($"/api/user-management/users/{Id}");
            _user = user;
            if (_user != null)
                _selectedRoleId = _user.RoleId.ToString();
        }
        catch { }
    }

    private async Task HandleSaveAsync()
    {
        _isSaving = true;
        try
        {
            var client = HttpClientFactory.CreateClient("AdminApi");
            var payload = new { _user?.Name, _user?.Email, RoleId = int.Parse(_selectedRoleId ?? "0") };
            await client.PutAsJsonAsync($"/api/user-management/users/{Id}", payload);
            Nav.NavigateTo("/admin/users");
        }
        catch { }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task HandleResetPasswordAsync()
    {
        _isResettingPassword = true;
        try
        {
            var client = HttpClientFactory.CreateClient("AdminApi");
            var response = await client.PostAsJsonAsync($"/api/user-management/users/{Id}/reset-password", new { });
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ResetPasswordResponse>();
                // Show password in modal/banner with copy button (same as CreateUser)
            }
        }
        finally
        {
            _isResettingPassword = false;
        }
    }

    private record ResetPasswordResponse(string GeneratedPassword);
    
    private class UserEditModel
    {
        public Guid Id { get; set; }
        [Required]
        public string Name { get; set; } = string.Empty;
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
        public int RoleId { get; set; }
    }

    private record RoleOption(int Id, string Name);
}
```

### Anti-Patterns to Avoid
- **Don't fetch user + roles separately in Create — fetch roles once in OnInitialized:** Reduces API calls; roles are static per tenant.
- **Don't bind password field to form — always auto-generate on server:** Avoids password complexity burden on admin; matches security best practice.
- **Don't show password in plaintext form field — only display once after creation/reset:** Reduces accidental screenshots/logs.
- **Don't mutate list items directly without API call — confirm deletion first:** Prevents inconsistency if delete fails silently.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Password generation | Custom random string function | BCrypt.Net's static GeneratePassword method or PasswordGenerator NuGet package | Edge cases: entropy, character set distribution, replay attacks |
| Password hashing | Plain `SHA256` or raw `BCrypt.Net.BCrypt.HashPassword` | LoginEndpoint pattern: use `BC.HashPassword(password)` (already proven) | Matches existing login verification; consistent salt rounds |
| Form validation on client + server | Hand-rolled attribute validators | FluentValidation + DataAnnotationsValidator | Reusable rules; CreateUser validator already exists as model |
| Data table sorting/filtering | Custom C# LINQ sorting | In-memory filtering from API response (for small datasets <1000 users) | Phase 12 scope: assume single-tenant <500 users; phase 13+ can add server-side sorting |
| JWT claim extraction | Manual `HttpContext.User.Claims.First(c => c.Type == "tenant_id")` | IUserContext service + ClaimsPrincipalExtensions | Type-safe, handles missing claims, reusable across endpoints |
| Modal confirmation dialogs | Custom JavaScript + modal HTML | TenantManagement.razor pattern: native Blazor EditForm modal with binding | Phase 9/11 proven; no extra dependencies |

## Backend API Endpoints (Required)

**NOTE:** CONTEXT.md flags 5 gaps in backend:
1. No ListUsers endpoint
2. No GetUser endpoint
3. No UpdateUser endpoint
4. No DeactivateUser endpoint
5. No ResetPassword endpoint
6. CreateUser doesn't hash password (plaintext storage)
7. CreateUser uses AppDbContext (central DB) not TenantDbContext (tenant DB)

### Endpoint Design

All endpoints must:
- Route under `/api/user-management/users/*`
- Require `[Authorize]` attribute
- Inject `IUserContext` to extract `TenantId` from JWT
- Use `TenantDbContext` via `ITenantDbContextFactory.CreateForTenant()`
- Apply soft delete via `IsDeleted` global query filter

**CreateUser (NEW — tenant-scoped)**
```csharp
// POST /api/user-management/users
Request: { Name, Email, RoleId }
Response: { UserId, GeneratedPassword }

- Generate secure password (12+ chars, mixed case/digits/special)
- Hash with BCrypt.Net.BCrypt.HashPassword()
- Create User entity via User.Create(TenantId, Name, Email, HashedPassword, Role)
- Return plaintext password once (NOT stored)
- Validation: Email must be unique within tenant, RoleId must exist in TenantDbContext
```

**ListUsers (NEW)**
```csharp
// GET /api/user-management/users
Response: List<{ UserId, Name, Email, RoleName, IsActive, CreatedAt }>

- Query TenantDbContext.Users.Where(u => u.TenantId == _userContext.TenantId)
- Global query filter handles !IsDeleted
- Join with Role for RoleName
- Order by CreatedAt DESC
```

**GetUser (NEW)**
```csharp
// GET /api/user-management/users/{id}
Response: { UserId, Name, Email, RoleId, RoleName, CreatedAt, IsActive }

- Query TenantDbContext.Users.Where(u => u.Id == id && u.TenantId == _userContext.TenantId)
- Return 404 if not found or different tenant
```

**UpdateUser (NEW)**
```csharp
// PUT /api/user-management/users/{id}
Request: { Name, Email, RoleId }
Response: { Success }

- Load user, verify tenant matches
- Update Name, Email, Role (no password field)
- SaveChangesAsync()
```

**DeactivateUser (NEW)**
```csharp
// DELETE /api/user-management/users/{id}
Response: { Success }

- Load user, verify tenant matches
- Set IsDeleted = true (soft delete)
- SaveChangesAsync()
- Global query filter hides from ListUsers automatically
```

**ResetPassword (NEW)**
```csharp
// POST /api/user-management/users/{id}/reset-password
Response: { GeneratedPassword }

- Load user, verify tenant matches
- Generate new password, hash with BCrypt
- Update user.Password with hash
- SaveChangesAsync()
- Return plaintext password once
```

**Fix CreateUser (MODIFICATION)**
- Add BCrypt hashing: `user.Password = BC.HashPassword(request.Password);`
- OR: Create new tenant-scoped endpoint and deprecate old one
- Recommendation: Create new endpoint at `/api/user-management/users`, keep old one for backward compatibility if platform admin still uses it

## Common Pitfalls

### Pitfall 1: Displaying Password in Logs or HTTP Response Body
**What goes wrong:** Developer logs plaintext password; password appears in request/response logs; admin can screenshot plaintext password without copying it.

**Why it happens:** Convenience during development; forgetting that passwords are highly sensitive data.

**How to avoid:** 
- Never log password values; log only `"Password set"` or `"Password reset"`
- Only return plaintext password in immediate success response (not persisted anywhere)
- Clear password from memory after response is sent (C# garbage collector handles this, but best practice is not to hold it)
- Display password in dismissible modal/banner, not in form field

**Warning signs:** 
- Tests that assert on plaintext password values
- Response classes that include password field
- API logs showing `"password": "abc123..."`

### Pitfall 2: Email Uniqueness Not Enforced at Database Level
**What goes wrong:** Two users created with same email (race condition); LoginEndpoint gets ambiguous result from `UserTenantIndex` lookup.

**Why it happens:** Validation only on application layer; database allows duplicate emails.

**How to avoid:**
- Add unique constraint at database level: `CREATE UNIQUE INDEX ix_user_email_tenant ON [User](Email, TenantId) WHERE IsDeleted = 0`
- Validate email is unique in both CreateUser and UpdateUser endpoints before SaveChangesAsync()
- Catch `DbUpdateException` with constraint violation and return 409 Conflict

**Warning signs:**
- No migrations adding unique index
- LoginEndpoint would throw `InvalidOperationException` if duplicate email exists
- Plan should include migration for this constraint

### Pitfall 3: TenantId Not Verified in Update/Delete
**What goes wrong:** Admin from TenantA somehow updates/deletes user from TenantB (cross-tenant data leak).

**Why it happens:** Developer forgets to load user and verify `user.TenantId == _userContext.TenantId` before modification.

**How to avoid:**
- Always load user with both ID and TenantId: `_dbContext.Users.Where(u => u.Id == id && u.TenantId == _userContext.TenantId)`
- If user not found, return 404 (don't differentiate between "not found" and "not authorized")
- Never modify user without this check

**Warning signs:**
- PUT/DELETE endpoints don't filter by TenantId
- Tests pass even with wrong tenant ID

### Pitfall 4: Mutable RecipeListItem Instead of Record
**What goes wrong:** List page accidentally persists optimistic UI update to database if subsequent delete fails silently (though unlikely given proper error handling).

**Why it happens:** Using mutable class instead of record; easier to accidentally `.IsActive = false` without re-fetching.

**How to avoid:**
- Consider using record type (immutable) for list items: `record UserListItem { ... }`
- On optimistic update, use `with` expression: `var updated = item with { IsActive = false }`
- Clear error banner before optimistic update; only close on successful response

**Warning signs:**
- UserListItem class is mutable (not record)
- No error banner cleared before optimistic UI change

### Pitfall 5: ListRoles Endpoint Returns Central DB Roles, Not Tenant Roles
**What goes wrong:** CreateUser dropdown shows "SuperAdmin" and "Admin" roles (central-only), but tenant roles are "Owner" and "TeleCaller". User selection doesn't match what gets saved.

**Why it happens:** Existing ListRoles endpoint uses AppDbContext; roles are central-managed. Tenant role assignments are in TenantDbContext.

**How to avoid:**
- CONTEXT.md mentions: "Role dropdown from ListRoles endpoint" — verify if this should be tenant-scoped
- If ListRoles remains central, UI should filter to only tenant-applicable roles (Owner, TeleCaller, not SuperAdmin/Admin)
- Plan must clarify: are tenants restricted to subset of central roles? Or do tenants have their own role definitions?
- Likely fix: Create new `GET /api/user-management/users/available-roles` that returns only roles valid for tenant users

**Warning signs:**
- ListRoles response includes SuperAdmin (shouldn't appear in tenant dropdown)
- Enum-based role IDs (1, 201, 301, 302) don't align with what tenant users can have

## Code Examples

### Backend: CreateUser Endpoint with BCrypt
```csharp
// IronMonkey.ApiService/Authentication/Endpoints/UserManagement/CreateUserEndpoint.cs
// Source: Based on CreateUser pattern + LoginEndpoint password handling

using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.ApiService.Authentication.Endpoints.UserManagement;

public class CreateUserEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/user-management/users", Handle)
        .WithSummary("Create new user in current tenant")
        .RequireAuthorization()
        .WithRequestValidation<Request>();

    public record Request(string Name, string Email, int RoleId);
    public record Response(Guid UserId, string GeneratedPassword);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MaxLength(200);
            RuleFor(x => x.Email).NotEmpty().EmailAddress();
            RuleFor(x => x.RoleId).GreaterThan(0);
        }
    }

    private static async Task<Results<Ok<Response>, ValidationError, NotFound, BadRequest>> Handle(
        Request request,
        IUserContext userContext,
        ITenantDbContextFactory tenantContextFactory,
        CancellationToken cancellationToken)
    {
        // Generate password
        var plainPassword = GeneratePassword();
        var hashedPassword = BC.HashPassword(plainPassword);

        // Open tenant DB
        await using var tenantDb = tenantContextFactory.CreateForTenant(
            userContext.TenantId); // TenantId from JWT

        // Check if email already exists in tenant
        var existingUser = await tenantDb.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken);

        if (existingUser != null)
            return TypedResults.BadRequest();

        // Load role
        var role = await tenantDb.Set<Role>()
            .FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);

        if (role == null)
            return TypedResults.NotFound();

        // Create user
        var user = User.Create(userContext.TenantId, request.Name, request.Email, hashedPassword, role);
        tenantDb.Users.Add(user);
        await tenantDb.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(user.Id, plainPassword));
    }

    private static string GeneratePassword()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
        var random = new System.Random();
        return new string(Enumerable.Range(0, 12)
            .Select(_ => chars[random.Next(chars.Length)])
            .ToArray());
    }
}
```

### Frontend: UserList with Deactivate Action
```csharp
// Source: RecipeList.razor pattern applied to users

private async Task HandleDeactivateAsync(Guid id)
{
    _isDeactivating = true;
    _deactivatingId = id;
    _errorBanner = null;
    try
    {
        var client = HttpClientFactory.CreateClient("AdminApi");
        var response = await client.DeleteAsync($"/api/user-management/users/{id}");
        if (response.IsSuccessStatusCode)
        {
            var item = _users.FirstOrDefault(u => u.Id == id);
            if (item is not null) item.IsActive = false;
        }
        else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            Nav.NavigateTo("/login");
        }
        else
        {
            _errorBanner = "Failed to deactivate user.";
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
```

### Frontend: Password Display Modal
```csharp
// Reusable component for showing generated password
// IronMonkey.Web/Components/Pages/Admin/Users/Shared/PasswordDisplay.razor

@if (!string.IsNullOrEmpty(GeneratedPassword))
{
    <div class="fixed inset-0 bg-black/50 flex items-center justify-center">
        <div class="bg-white rounded-lg shadow-lg p-6 max-w-md">
            <h2 class="text-lg font-bold text-slate-900 mb-4">User Created Successfully</h2>
            <p class="text-sm text-slate-600 mb-4">Share this password (displayed once):</p>
            <div class="flex items-center gap-2 mb-6">
                <code class="flex-1 px-3 py-2 bg-slate-50 border border-slate-200 rounded font-mono text-sm">@GeneratedPassword</code>
                <button @onclick="CopyToClipboard" class="px-3 py-2 bg-indigo-600 text-white rounded hover:bg-indigo-500">
                    Copy
                </button>
            </div>
            <button @onclick="Close" class="w-full px-4 py-2 bg-slate-200 text-slate-900 rounded hover:bg-slate-300">
                Done
            </button>
        </div>
    </div>
}

@code {
    [Parameter] public string? GeneratedPassword { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private async Task CopyToClipboard()
    {
        // Use JS interop to copy GeneratedPassword to clipboard
        // Implementation uses Blazor JavaScript interop (not included here)
    }

    private async Task Close() => await OnClose.InvokeAsync();
}
```

## Validation Architecture

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 + Moq 4.20.72 |
| Config file | None — tests follow naming convention (*.Tests.csproj, *Tests.cs files) |
| Quick run command | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~UserManagementTests" -x` |
| Full suite command | `dotnet test IronMonkey.Tests` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| USUI-01 | ListUsers returns all active users for current tenant | Integration | `dotnet test IronMonkey.Tests --filter "UserManagementTests.List_returns_active_users" -x` | ❌ Wave 0 |
| USUI-01 | ListUsers filters out deactivated by default | Integration | `dotnet test IronMonkey.Tests --filter "UserManagementTests.List_hides_deactivated" -x` | ❌ Wave 0 |
| USUI-02 | CreateUser generates unique password, hashes with BCrypt | Integration | `dotnet test IronMonkey.Tests --filter "UserManagementTests.Create_hashes_password_with_bcrypt" -x` | ❌ Wave 0 |
| USUI-02 | CreateUser rejects duplicate email in same tenant | Integration | `dotnet test IronMonkey.Tests --filter "UserManagementTests.Create_rejects_duplicate_email" -x` | ❌ Wave 0 |
| USUI-03 | UpdateUser modifies name/email/role without changing password | Integration | `dotnet test IronMonkey.Tests --filter "UserManagementTests.Update_preserves_password" -x` | ❌ Wave 0 |
| USUI-03 | ResetPassword generates new password, hashes with BCrypt | Integration | `dotnet test IronMonkey.Tests --filter "UserManagementTests.Reset_password_generates_new_hash" -x` | ❌ Wave 0 |
| USUI-04 | DeactivateUser soft-deletes (IsDeleted=true) | Integration | `dotnet test IronMonkey.Tests --filter "UserManagementTests.Deactivate_sets_is_deleted" -x` | ❌ Wave 0 |
| USUI-04 | DeactivateUser hides from ListUsers | Integration | `dotnet test IronMonkey.Tests --filter "UserManagementTests.Deactivate_hidden_from_list" -x` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~UserManagementTests" -x`
- **Per wave merge:** `dotnet test IronMonkey.Tests`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `IronMonkey.Tests/Integration/UserManagementTests.cs` — Covers USUI-01 through USUI-04
  - Create user with unique password generation
  - List users (active, deactivated with filter toggle)
  - Update user details and role
  - Reset password
  - Deactivate user (soft delete)
  - Cross-tenant isolation (verify user from TenantA cannot access TenantB users)
- [ ] Endpoint stubs for testing: `CreateUserEndpoint.HandleForTest()`, `UpdateUserEndpoint.HandleForTest()`, etc. (pattern from Phase 11 TenantEndpoints)

## Sources

### Primary (HIGH confidence)
- **Project Code:** LoginEndpoint.cs, User.cs, Role.cs, RecipeList.razor, RecipeCreate.razor, RecipeEdit.razor, BearerTokenHandler.cs, IUserContext.cs, ClaimsPrincipalExtensions.cs — direct inspection of working patterns
- **CLAUDE.md:** Project instructions including Blazor Server, Tailwind CSS, BCrypt usage, JWT "tenant_id" claim structure
- **CONTEXT.md:** Phase 12 decisions (D-01 through D-21) specifying page structure, form fields, password UX, soft delete pattern

### Secondary (MEDIUM confidence)
- **IronMonkey.Tests/IronMonkey.Tests.csproj:** xUnit 2.9.3, Moq 4.20.72, Testcontainers — verified from project file
- **AdminSidebar.razor:** Navigation structure shows `/admin/users` route already present, confirming routing ready

### Tertiary (Research-based)
- **BCrypt.Net:** Recommended based on LoginEndpoint using `BC.Verify()` — assume same library available
- **Password generation:** Custom implementation shown; could use NuGet `PasswordGenerator` package for production, but custom approach is simple and avoids dependency
- **Unique email constraint:** Standard database best practice for preventing duplicates

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all libraries already in use by Phase 9/10/11
- Architecture: HIGH — patterns directly derived from RecipeList/Create/Edit pages
- Pitfalls: HIGH — identified from CONTEXT.md gaps and common multi-tenant issues
- Backend endpoints: MEDIUM — detailed design inferred from CONTEXT.md requirements; actual implementation details TBD during planning

**Research date:** 2026-04-01
**Valid until:** 2026-04-15 (14 days — stack and patterns are stable, valid through phase planning/execution)
**Assumptions:** 
- BCrypt.Net package is available (used by LoginEndpoint)
- TenantDbContext factory pattern is functional (proven by existing leads/recipes)
- Blazor Server EditForm + DataAnnotationsValidator + validation messages work as documented

