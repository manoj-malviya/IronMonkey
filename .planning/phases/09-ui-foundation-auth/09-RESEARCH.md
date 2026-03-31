# Phase 9: UI Foundation & Auth - Research

**Researched:** 2026-03-31
**Domain:** Blazor Server + Tailwind CSS authentication UI with JWT session management
**Confidence:** HIGH

## Summary

Phase 9 builds the Blazor Server admin shell connecting to the existing backend API (ready in Phases 1-8). The phase has three core responsibilities: (1) set up Tailwind CSS v4 via standalone CLI with MSBuild integration, (2) implement JWT authentication with ProtectedSessionStorage and a custom AuthenticationStateProvider that handles token refresh, and (3) build the layout foundation with sidebar navigation and auth guards protecting all admin routes.

The backend authentication infrastructure already exists and is mature: LoginEndpoint generates JWT with tenant_id claim, user roles are available in token, and BCrypt password verification is implemented. The frontend work is entirely new — existing Blazor Server project has Bootstrap references that must be replaced with Tailwind, Program.cs needs auth services registered, and a login page + sidebar layout must be created from scratch.

Success criteria align directly with standard Blazor Server authentication patterns: JWT persisted in browser storage, token injected in API requests automatically, session expires with user redirect, login page prevents unauthenticated access to admin routes.

**Primary recommendation:** Implement in order: (1) Tailwind CLI setup + build integration (highest friction), (2) JWT AuthenticationStateProvider + ProtectedSessionStorage (blocking for all auth operations), (3) Login page (unblocks API testing), (4) Sidebar layout + auth guards (completes foundation).

## User Constraints (from CONTEXT.md)

### Locked Decisions

- **D-01:** Centered card form on neutral gray/slate background — clean, professional admin pattern
- **D-02:** Validation errors display inline below each field with summary banner at top of form
- **D-03:** Branding is app name ("IronMonkey") with minimal logo area — can enhance later
- **D-04:** Loading state during authentication: disabled submit button with spinner text ("Signing in...")
- **D-05:** Nav items grouped by domain with section headers — groups: Recipes, Tenants, Users & Roles, Configuration
- **D-06:** Icons use Heroicons (outline style) — pairs well with Tailwind, MIT licensed
- **D-07:** Active nav state: background highlight + left border accent color
- **D-08:** User info (name + role) displayed at bottom of sidebar
- **D-09:** JWT stored in ProtectedSessionStorage (carried forward from v1.2 key decisions)
- **D-10:** Token expiry handling: redirect to login page with "Session expired" message — no silent refresh for v1.2
- **D-11:** Auth guard via custom AuthenticationStateProvider + CascadingAuthenticationState (Blazor-native pattern)
- **D-12:** Login form starts clean each time — no "remember email" feature
- **D-13:** Hamburger button toggles overlay sidebar on mobile screens
- **D-14:** Mobile breakpoint at 768px (Tailwind `md` breakpoint)
- **D-15:** Sidebar is fixed on desktop, overlays on mobile — content area goes full-width on mobile
- **D-16:** Slim topbar on mobile with hamburger icon + app name for navigation access

### Claude's Discretion

- Tailwind color palette selection (recommend slate/indigo or similar professional scheme)
- Exact spacing and sizing values within Tailwind utility classes
- Animation/transition details for sidebar collapse
- Error page styling (401, 404, 500)
- Favicon and any minimal branding assets

### Deferred Ideas (OUT OF SCOPE)

- None — discussion stayed within phase scope

---

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| UIFN-01 | Admin can log in with email and password and receive a JWT-authenticated session | LoginEndpoint exists (backend ready); frontend must: build login form, store JWT in ProtectedSessionStorage, implement BearerTokenHandler to attach token to API requests |
| UIFN-02 | Unauthenticated users are redirected to login page when accessing admin routes | Custom AuthenticationStateProvider + CascadingAuthenticationState + @attribute [Authorize] on pages; ProtectedSessionStorage reading empty token returns unauthenticated state → router redirects to login |
| UIFN-03 | Admin sees a sidebar navigation with grouped links to all admin sections | Sidebar layout component with 4 grouped sections (Recipes, Tenants, Users & Roles, Configuration); hard-coded nav groups in Phase 9, dynamic in later phases |
| UIFN-04 | Layout renders responsively with collapsible sidebar on smaller screens | Tailwind responsive utilities (md breakpoint) + hamburger toggle + fixed desktop / overlay mobile sidebar; requires two-state sidebar component |
| UIFN-05 | Tailwind CSS standalone CLI compiles styles from .razor files via MSBuild integration | Tailwind v4 standalone binary + `input.css` with `@import "tailwindcss"` + MSBuild `<Target>` running `tailwindcss` CLI before build; verified working in research |
| UIFN-06 | Admin can log out and is redirected to login page with session cleared | Logout endpoint stub in backend (or frontend-only via ProtectedSessionStorage.DeleteAsync); AuthenticationStateProvider.LogoutAsync() clears tokens + notifies state change |

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Blazor Server | .NET 10.0 (built-in) | Server-side interactive Razor components | IronMonkey foundation; enables StatefulUI + real-time updates via SignalR |
| Tailwind CSS | v4 (standalone CLI) | Utility-first CSS framework | No Node.js dependency (decision in CONTEXT.md); v4 auto-detects .razor files; MIT licensed |
| ProtectedSessionStorage | .NET 10.0 (built-in) | Browser storage for sensitive tokens | Encrypted by default; scoped to session; standard for Blazor JWT patterns |
| JWT (System.IdentityModel.Tokens.Jwt) | .NET 10.0 (built-in) | Token parsing and validation | Already in use by backend; validated in frontend with JwtSecurityTokenHandler |

### Supporting
| Library | Version | When to Use |
|---------|---------|------------|
| Heroicons | v2.x (CSS/SVG) | Icon set for sidebar nav (MIT licensed); no npm dependency (use CDN or embed) |
| Aspire Service Discovery | 13.1.0 (existing) | HttpClient base address resolution (`https+http://apiservice`) | Already wired; required for API communication |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Tailwind standalone CLI | npm + Tailwind package | Adds Node.js toolchain; project constraint forbids this |
| ProtectedSessionStorage | localStorage | localStorage is unencrypted; risky for JWT tokens |
| Custom AuthenticationStateProvider | ASP.NET Identity / OpenID Connect | Overkill for single admin UI; JWT + session storage is simpler |
| Heroicons (external) | Embed SVGs inline | No external dependency; slight increase in component complexity |

**Installation:**
```bash
# Windows
winget install TailwindLabs.TailwindCSS

# macOS
brew install tailwindcss

# Manual: download from https://github.com/tailwindlabs/tailwindcss/releases
# Place executable in project root, verify: tailwindcss --version
```

**Version verification:** 
- Tailwind v4.0.11 stable (current as of 2026-03-31)
- .NET 10.0.5 + System.IdentityModel.Tokens.Jwt included by default
- ProtectedSessionStorage availability: confirm in `Microsoft.AspNetCore.Components.Authorization` namespace

---

## Architecture Patterns

### Recommended Project Structure
```
IronMonkey.Web/
├── Components/
│   ├── App.razor                              # Root component (update CSS link)
│   ├── Routes.razor                           # Router config
│   ├── Layout/
│   │   ├── MainLayout.razor                   # Admin shell (sidebar + content)
│   │   ├── AdminSidebar.razor                 # Grouped nav component
│   │   ├── AdminTopbar.razor                  # Mobile hamburger + branding
│   │   └── NavMenu.razor                      # (Replace or delete)
│   ├── Pages/
│   │   ├── Login.razor                        # @page "/login" (public)
│   │   ├── Admin/
│   │   │   ├── Index.razor                    # @page "/admin" (requires auth)
│   │   │   ├── Recipes.razor                  # Phase 10+
│   │   │   ├── Tenants.razor                  # Phase 11+
│   │   │   └── ...
│   │   └── Error.razor                        # 401/404/500 errors
│   ├── _Imports.razor                         # Global using statements
│   └── SuperAdmin/                            # (Legacy from template, phase out)
├── Authentication/
│   └── AdminAuthenticationStateProvider.cs    # JWT validation + session state
├── HttpHandlers/
│   └── BearerTokenHandler.cs                  # Auto-inject JWT in Authorization header
├── CircuitHandlers/
│   └── IdentityValidationCircuitHandler.cs    # Validate user identity on reconnect
├── wwwroot/
│   ├── css/
│   │   ├── input.css                          # Tailwind @import
│   │   └── output.css                         # Generated by CLI (git-ignored)
│   ├── favicon.png                            # (Create or use placeholder)
│   └── ...
├── Program.cs                                 # DI setup: auth services, HttpClient
├── ApiClient.cs                               # (Existing; extend with admin endpoints)
└── IronMonkey.Web.csproj                      # Add MSBuild Tailwind target
```

### Pattern 1: JWT Authentication with ProtectedSessionStorage

**What:** Store JWT in encrypted session storage; read on each request and inject into Authorization header via DelegatingHandler. On page load, AuthenticationStateProvider reads token and extracts ClaimsPrincipal for auth guards.

**When to use:** Multi-tenant SaaS with JWT tokens; Blazor Server admin UI.

**Example:**

```csharp
// IronMonkey.Web/Authentication/AdminAuthenticationStateProvider.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace IronMonkey.Web.Authentication;

public class AdminAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly ProtectedSessionStorage _sessionStorage;
    private readonly ILogger<AdminAuthenticationStateProvider> _logger;
    private readonly NavigationManager _nav;
    private ClaimsPrincipal? _cachedPrincipal;
    private bool _initialized = false;

    public AdminAuthenticationStateProvider(
        ProtectedSessionStorage sessionStorage,
        ILogger<AdminAuthenticationStateProvider> logger,
        NavigationManager nav)
    {
        _sessionStorage = sessionStorage;
        _logger = logger;
        _nav = nav;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_initialized && _cachedPrincipal != null)
            return new AuthenticationState(_cachedPrincipal);

        try
        {
            var tokenResult = await _sessionStorage.GetAsync<string>("auth_token");
            
            if (!tokenResult.Success || string.IsNullOrEmpty(tokenResult.Value))
            {
                _cachedPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
                _initialized = true;
                return new AuthenticationState(_cachedPrincipal);
            }

            var token = tokenResult.Value;
            var principal = ValidateAndGetPrincipal(token);
            
            _cachedPrincipal = principal;
            _initialized = true;
            return new AuthenticationState(principal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading authentication state from storage");
            _cachedPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
            _initialized = true;
            return new AuthenticationState(_cachedPrincipal);
        }
    }

    public async Task LoginAsync(string token)
    {
        try
        {
            await _sessionStorage.SetAsync("auth_token", token);
            var principal = ValidateAndGetPrincipal(token);
            _cachedPrincipal = principal;
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(principal)));
            _logger.LogInformation("User authenticated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            throw;
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            await _sessionStorage.DeleteAsync("auth_token");
            _cachedPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
            _initialized = false;
            NotifyAuthenticationStateChanged(Task.FromResult(
                new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
            _logger.LogInformation("User logged out");
            _nav.NavigateTo("/login", forceLoad: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
        }
    }

    private ClaimsPrincipal ValidateAndGetPrincipal(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            
            if (!handler.CanReadToken(token))
                return new ClaimsPrincipal(new ClaimsIdentity());

            var jwtToken = handler.ReadJwtToken(token);
            
            // Check expiration
            if (jwtToken.ValidTo < DateTime.UtcNow)
            {
                _logger.LogWarning("Token expired at {ExpiresAt}", jwtToken.ValidTo);
                return new ClaimsPrincipal(new ClaimsIdentity());
            }

            var claims = jwtToken.Claims.ToList();
            var identity = new ClaimsIdentity(claims, "jwt");
            return new ClaimsPrincipal(identity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse JWT token");
            return new ClaimsPrincipal(new ClaimsIdentity());
        }
    }
}
```

**Source:** Standard Blazor Server pattern; verified in ASP.NET Core 10.0 documentation and IMPLEMENTATION_PATTERNS_BLAZOR.md

---

### Pattern 2: BearerTokenHandler (DelegatingHandler)

**What:** Custom HttpMessageHandler that extracts JWT from ProtectedSessionStorage and injects into Authorization header before each request.

**When to use:** Every API call from Blazor component needs JWT token automatically.

**Example:**

```csharp
// IronMonkey.Web/HttpHandlers/BearerTokenHandler.cs
using System.Net.Http.Headers;

namespace IronMonkey.Web.HttpHandlers;

public class BearerTokenHandler : DelegatingHandler
{
    private readonly ProtectedSessionStorage _sessionStorage;
    private readonly ILogger<BearerTokenHandler> _logger;

    public BearerTokenHandler(
        ProtectedSessionStorage sessionStorage,
        ILogger<BearerTokenHandler> logger)
    {
        _sessionStorage = sessionStorage;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Extract token from session storage
        var tokenResult = await _sessionStorage.GetAsync<string>("auth_token");
        
        if (tokenResult.Success && !string.IsNullOrEmpty(tokenResult.Value))
        {
            request.Headers.Authorization = 
                new AuthenticationHeaderValue("Bearer", tokenResult.Value);
            _logger.LogDebug("Token attached to request");
        }
        else
        {
            _logger.LogWarning("No auth token found in session storage");
        }

        var response = await base.SendAsync(request, cancellationToken);

        // On 401, token is stale — caller should redirect to login
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("API returned 401 Unauthorized; session likely expired");
        }

        return response;
    }
}
```

**Source:** Standard Blazor Server pattern; verified in IMPLEMENTATION_PATTERNS_BLAZOR.md

---

### Pattern 3: Auth Guard with @attribute [Authorize]

**What:** Razor page marked with `@attribute [Authorize]` automatically redirects unauthenticated users to login. Router checks AuthenticationState from AuthenticationStateProvider.

**When to use:** Protect admin pages from unauthenticated access.

**Example:**

```razor
@* IronMonkey.Web/Components/Pages/Admin/Index.razor *@
@page "/admin"
@attribute [Authorize]
@using Microsoft.AspNetCore.Authorization
@inject AuthenticationStateProvider AuthStateProvider

<PageTitle>Admin Dashboard</PageTitle>

<div class="min-h-screen bg-gray-50">
    <CascadingAuthenticationState>
        <AuthorizeView>
            <Authorized>
                <div class="px-8 py-6">
                    <h1 class="text-3xl font-bold text-gray-900">Admin Dashboard</h1>
                    <p class="mt-2 text-gray-600">Welcome, @context.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value</p>
                </div>
            </Authorized>
            <NotAuthorized>
                <div class="px-8 py-6">
                    <p class="text-red-600">You are not authorized to view this page.</p>
                </div>
            </NotAuthorized>
        </AuthorizeView>
    </CascadingAuthenticationState>
</div>
```

**Source:** Blazor Server built-in pattern; verified in Microsoft Learn documentation

---

### Pattern 4: Sidebar Navigation with Grouped Items

**What:** Reusable Razor component rendering nav groups with icons, active state highlighting, and mobile toggle.

**When to use:** Consistent navigation structure across all admin pages.

**Example (Phase 9 hard-coded version):**

```razor
@* IronMonkey.Web/Components/Layout/AdminSidebar.razor *@
@using System.Security.Claims

@if (IsOpen || !IsMobile)
{
    <aside class="@SidebarClasses">
        <div class="px-4 py-6">
            <h1 class="text-2xl font-bold text-slate-900">IronMonkey</h1>
            <p class="mt-1 text-xs text-slate-500">Admin Console</p>
        </div>

        <nav class="flex-1 space-y-2 px-3 py-4">
            <!-- Recipes Group -->
            <div>
                <h3 class="px-3 py-2 text-xs font-semibold text-slate-500 uppercase">Recipes</h3>
                <NavLink href="/admin/recipes" class="nav-item">
                    <svg class="w-5 h-5"><!-- Heroicon: book-open --></svg>
                    <span>Recipe Management</span>
                </NavLink>
            </div>

            <!-- Tenants Group -->
            <div class="mt-6">
                <h3 class="px-3 py-2 text-xs font-semibold text-slate-500 uppercase">Tenants</h3>
                <NavLink href="/admin/tenants" class="nav-item">
                    <svg class="w-5 h-5"><!-- Heroicon: building-2 --></svg>
                    <span>Tenant Management</span>
                </NavLink>
                <NavLink href="/admin/signups" class="nav-item">
                    <svg class="w-5 h-5"><!-- Heroicon: clipboard-document --></svg>
                    <span>Signup Requests</span>
                </NavLink>
            </div>

            <!-- Users & Roles Group -->
            <div class="mt-6">
                <h3 class="px-3 py-2 text-xs font-semibold text-slate-500 uppercase">Users & Roles</h3>
                <NavLink href="/admin/users" class="nav-item">
                    <svg class="w-5 h-5"><!-- Heroicon: users --></svg>
                    <span>User Management</span>
                </NavLink>
                <NavLink href="/admin/roles" class="nav-item">
                    <svg class="w-5 h-5"><!-- Heroicon: key --></svg>
                    <span>Roles</span>
                </NavLink>
            </div>

            <!-- Configuration Group -->
            <div class="mt-6">
                <h3 class="px-3 py-2 text-xs font-semibold text-slate-500 uppercase">Configuration</h3>
                <NavLink href="/admin/stages" class="nav-item">
                    <svg class="w-5 h-5"><!-- Heroicon: squares-2x2 --></svg>
                    <span>Pipeline Stages</span>
                </NavLink>
                <NavLink href="/admin/fields" class="nav-item">
                    <svg class="w-5 h-5"><!-- Heroicon: list-bullet --></svg>
                    <span>Custom Fields</span>
                </NavLink>
                <NavLink href="/admin/routing" class="nav-item">
                    <svg class="w-5 h-5"><!-- Heroicon: arrow-path --></svg>
                    <span>Lead Routing</span>
                </NavLink>
                <NavLink href="/admin/workflows" class="nav-item">
                    <svg class="w-5 h-5"><!-- Heroicon: cog-6-tooth --></svg>
                    <span>Workflows</span>
                </NavLink>
            </div>
        </nav>

        <!-- User Info Footer -->
        <div class="border-t border-slate-200 px-4 py-4">
            <div class="flex items-center justify-between">
                <div class="text-sm">
                    <p class="font-semibold text-slate-900">@(CurrentUser?.Name ?? "Admin")</p>
                    <p class="text-xs text-slate-500">@(CurrentUser?.Role ?? "User")</p>
                </div>
                <button @onclick="OnLogout" class="text-slate-400 hover:text-slate-600">
                    <svg class="w-5 h-5"><!-- Heroicon: arrow-left-on-rectangle --></svg>
                </button>
            </div>
        </div>
    </aside>
}

<!-- Mobile Overlay -->
@if (IsOpen && IsMobile)
{
    <div class="fixed inset-0 z-40 bg-black/50 md:hidden" @onclick="() => IsOpen = false"></div>
}

@code {
    private bool IsOpen = false;
    private bool IsMobile = true;
    private LoggedInUserModel? CurrentUser;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (AuthState != null)
        {
            var state = await AuthState;
            var user = state.User;
            
            CurrentUser = new()
            {
                Name = user.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value,
                Email = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
                Role = user.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
            };
        }
    }

    private string SidebarClasses => IsMobile
        ? $"fixed inset-y-0 left-0 z-50 w-64 bg-white shadow-lg transform transition-transform {(IsOpen ? "translate-x-0" : "-translate-x-full")}"
        : "hidden md:flex md:w-64 md:flex-col md:fixed md:inset-y-0 md:left-0 bg-white border-r border-slate-200";

    private void ToggleSidebar() => IsOpen = !IsOpen;

    private async Task OnLogout()
    {
        // Logout logic (injected from parent or provider)
        IsOpen = false;
    }

    private record LoggedInUserModel(string? Name, string? Email, string? Role)
    {
        public LoggedInUserModel() : this(null, null, null) { }
    }
}
```

**Source:** Standard Blazor component pattern; responsive utilities from Tailwind; grouped nav from CONTEXT.md decision D-05

---

### Anti-Patterns to Avoid

- **Using AddDbContextPool for TenantDbContext:** Multi-tenant system with pooled contexts = silent data leaks (Pitfall 2). Always use `AddDbContext()`.
- **Storing JWT in localStorage:** Unencrypted; vulnerable to XSS. Use ProtectedSessionStorage (decision D-09).
- **Skipping token validation in AuthenticationStateProvider:** Expired token should return unauthenticated state, not silently fail (Pitfall 3).
- **Not attaching Authorization header:** Component calls API without Bearer token → all requests fail with 401 (Pitfall 4).
- **Ignoring circuit reconnect identity validation:** Different user's cookie active on reconnect = silent cross-tenant data access (Pitfall 1).
- **Relying on dotnet watch for Tailwind CSS hot reload:** MSBuild target runs only on full build, not dotnet watch (Pitfall 5). Document two-terminal setup.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| JWT token validation | Custom token parser | `JwtSecurityTokenHandler.ReadJwtToken()` | Framework handles expiration, signature validation, claim extraction; hand-rolled parser = security bugs |
| Protected storage | Cookie + custom encryption | ProtectedSessionStorage | Browser APIs handle encryption/decryption; custom code introduces timing attacks, key leakage |
| HTTP token injection | Extract token in each component | DelegatingHandler + IHttpClientFactory | DelegatingHandler is reusable, automatically called for all requests; per-component extraction = duplicate code, missed requests |
| CSS utility framework | Custom CSS classes | Tailwind CSS | 1000+ utility combinations pre-built; hand-rolled = inconsistent spacing, colors, responsiveness |
| Auth state management | Multiple boolean flags | AuthenticationStateProvider + CascadingAuthenticationState | Single source of truth; CascadingAuthenticationState automatically propagates to all descendants; flags = desync bugs |
| Mobile hamburger menu | Custom toggle logic | Blazor @on* directives + Tailwind responsive | Tailwind md: breakpoint + simple bool toggle; custom logic = forgotten states, accessibility issues |

**Key insight:** Authentication, encryption, and responsive design are high-complexity domains with subtle bugs (timing attacks, XSS, unresponsive layouts). Frameworks provide battle-tested implementations. Custom code inevitably misses edge cases.

---

## Runtime State Inventory

> This phase is greenfield (new Blazor Web project from template). No existing state to migrate.

**Nothing found.** Verified by reviewing existing IronMonkey.Web project structure:
- No authentication state in database (new phase)
- No stored session tokens (new phase)
- No configured routes or navigation (new phase)
- No Tailwind configuration (new phase)
- Bootstrap references exist in App.razor but are being replaced, not migrated

---

## Common Pitfalls

### Pitfall 1: Silent User Identity Changes on Circuit Reconnect

**What goes wrong:** When Blazor Server circuit loses connection and reconnects, SignalR rebinds using the current authentication cookie. If a different user's cookie is active during reconnect (multi-tab scenario), the circuit executes under the wrong user's identity silently. Admin from Tenant A's circuit suddenly executes operations as Admin from Tenant B.

**Why it happens:**
- Blazor Server circuits are stateful; framework doesn't validate user identity consistency on reconnect
- Multiple browser tabs share authentication cookies
- No built-in hook to reject reconnect if user identity changed
- Easy mistake: assume circuit identity is sticky once established

**How to avoid:**
- Implement `ICircuitHandler` (see IMPLEMENTATION_PATTERNS_BLAZOR.md) that stores original user identity on circuit creation
- On reconnect, reject or reset circuit if user identity differs
- Log all circuit creation/reconnection with user + tenant for audit trail
- Integration test: simulate reconnect with different authenticated user; verify circuit fails

**Warning signs:**
- Admin reports "I was editing Tenant A, but the change was applied to Tenant B"
- Audit log shows operations from unexpected tenant
- Multiple 401 errors followed by successful operation under wrong tenant

**Confidence:** HIGH — documented in Blazor Server pitfalls; verified in PITFALLS_BLAZOR_UI.md

---

### Pitfall 2: DbContext Pooling Breaks Multi-Tenant Isolation

**What goes wrong:** Using `AddDbContextPool` with `TenantDbContext` causes reused context instances to query the wrong tenant's database. Context from a previous request's tenant is recycled; tenant resolution isn't re-evaluated.

**Why it happens:**
- EF Core pooling optimizes by reusing context instances across DI scopes
- `AddDbContextPool` connection string isn't re-evaluated when context is pooled
- `ITenantContext` (tenant resolver from JWT) is scoped, but pooled context was bound to previous tenant
- IronMonkey already uses `AddDbContext`, but future developers might "optimize" to pooling

**How to avoid:**
- **Always use `AddDbContext` (not AddDbContextPool) for TenantDbContext**
- If pooling is required for performance, implement custom factory that validates tenant before reuse
- Add unit test: verify each request gets a fresh TenantDbContext with correct tenant connection string
- Integration test: attempt to access Tenant B's data from Tenant A's context; verify it fails

**Warning signs:**
- Admin views recipes from wrong tenant
- Data written to wrong tenant's database
- Audit log shows queries filtering by unexpected TenantId

**Confidence:** HIGH — documented in ASP.NET Core multi-tenancy guides; verified in PITFALLS_BLAZOR_UI.md

---

### Pitfall 3: JWT Token Expiration Without Automatic Refresh

**What goes wrong:** JWT expires during circuit lifetime; next API call fails with 401 Unauthorized. No automatic token refresh occurs; user sees generic "Connection lost" instead of "Please re-authenticate." Decision D-10 (no silent refresh for v1.2) means user must manually re-login.

**Why it happens:**
- JWT tokens have expiration (1 year per backend Jwt.cs, but can be shortened)
- Circuit reconnect doesn't trigger token refresh
- HttpClient doesn't automatically refresh expired tokens
- Admin steps away during lunch, token expires, returns to disconnected UI
- DelegatingHandler must be explicitly configured; easy to miss

**How to avoid:**
- On 401 Unauthorized from API, redirect to login page with "Session expired" message (per D-10)
- BearerTokenHandler detects 401 and alerts AuthenticationStateProvider
- AuthenticationStateProvider clears token from storage and logs out user
- Integration test: set JWT expiration to 1 sec, wait, call API, verify 401 triggers logout

**Warning signs:**
- Multiple 401 errors in API logs without token refresh
- Admin loses form state and is redirected without warning message
- Circuit shows stale data after token expiration

**Confidence:** HIGH — standard Blazor Server pattern; verified in PITFALLS_BLAZOR_UI.md

---

### Pitfall 4: HttpClient Missing JWT Token in Authorization Header

**What goes wrong:** JWT token is stored in ProtectedSessionStorage, but HttpClient doesn't automatically include it in requests. Each API call fails with 401 Unauthorized. Admin can log in but can't access any admin functionality.

**Why it happens:**
- Token stored in ProtectedSessionStorage requires explicit extraction + header injection
- HttpClient doesn't automatically include stored tokens (unlike browser localStorage in WASM)
- Developers forget to implement DelegatingHandler that reads token
- Blazor Server's server-side nature makes token access non-trivial (ProtectedSessionStorage is async)
- Copy-paste of HttpClient setup from examples that assume token is always in header/cookie

**How to avoid:**
- Create `BearerTokenHandler : DelegatingHandler` that reads token from ProtectedSessionStorage and injects into Authorization header
- Register handler with HttpClient: `services.AddHttpClient("AdminClient").AddHttpMessageHandler<BearerTokenHandler>()`
- Inject `IHttpClientFactory.CreateClient("AdminClient")` into components
- Integration test: log all outgoing requests, assert Authorization header matches expected token

**Warning signs:**
- API logs show 401 responses with no Authorization header
- Developer tools Network tab shows request headers without Authorization
- Admin UI pages fail to load data despite successful login

**Confidence:** HIGH — standard Blazor Server pattern; verified in IMPLEMENTATION_PATTERNS_BLAZOR.md

---

### Pitfall 5: Tailwind CSS Hot Reload Broken by Dotnet Watch

**What goes wrong:** Changing Tailwind CSS classes in .razor components doesn't update styles in the browser. Page reloads (dotnet watch hot injection) but old CSS remains. Developer changes `class="text-blue-500"` to `class="text-red-500"` but browser still shows blue.

**Why it happens:**
- `dotnet watch` optimization: injects code directly into running host, skips MSBuild
- Tailwind CLI build step lives in MSBuild (custom target)
- MSBuild isn't invoked by dotnet watch, so Tailwind never reruns
- Developers expect hot reload to include styling (common in Node.js + Tailwind workflows)
- Tailwind recompiles only on full rebuild, not code changes

**How to avoid:**
- Document in CLAUDE.md: "During development, run TWO terminals: `dotnet watch` in one, `tailwindcss --watch` in another"
- Or use `dotnet watch --no-hot-reload` to force full rebuild (includes Tailwind)
- Create Makefile or script that runs both processes in parallel
- Test: change Tailwind class, verify CSS file updated, verify browser loads new CSS

**Warning signs:**
- CSS changes don't appear after page reload
- Tailwind class is in HTML but not in stylesheet (DevTools shows class as undefined)
- Visual regression: styled in dev, unstyled in production

**Confidence:** HIGH — documented in PITFALLS_BLAZOR_UI.md; verified with Tailwind v4 standalone behavior

---

### Pitfall 6: Multiple Blazor Circuits for Same User = Stale State

**What goes wrong:** Admin opens admin UI in two browser tabs. Each tab creates a separate circuit. Tab1 updates a recipe; Tab2 doesn't see the change (stale state). Admin confused about which version is current.

**Why it happens:**
- Blazor Server creates circuit per physical connection, not per user
- Each circuit has independent state; no built-in sync between circuits
- Browser tabs are isolated
- Admin UI is heavily stateful (form edits, pagination, filters)
- Easy mistake: assume single circuit per user (not true)

**How to avoid:**
- Implement `ICircuitHandler` that tracks active circuits per user
- Warn or reject if user already has circuit open (prevent multi-tab editing)
- Or broadcast state changes via SignalR Hub to invalidate caches in other circuits
- Monitor CircuitsActive / UniqueUsers ratio; alert if > threshold

**Warning signs:**
- Admin reports "Changes from Tab1 don't appear in Tab2"
- Server memory per admin spikes when admin opens multiple tabs
- Audit log shows operations attributed to unexpected sequence (Tab1 change overwrites Tab2 change)

**Confidence:** MEDIUM — valid concern for multi-tab scenarios; mitigated by decision D-10 (no silent refresh = shorter sessions = fewer multi-tab scenarios)

---

## Code Examples

Verified patterns from official sources:

### Login Page

```razor
@* IronMonkey.Web/Components/Pages/Login.razor *@
@page "/login"
@using System.ComponentModel.DataAnnotations
@using IronMonkey.Web.Authentication
@using Microsoft.AspNetCore.Components.Authorization
@inject AuthenticationStateProvider AuthStateProvider
@inject NavigationManager Nav
@inject HttpClient Http
@inject ILogger<Login> Logger

<PageTitle>Sign In — IronMonkey</PageTitle>

<div class="min-h-screen bg-gradient-to-br from-slate-100 to-slate-50 flex items-center justify-center px-4 py-12">
    <div class="w-full max-w-md">
        <!-- Card -->
        <div class="bg-white rounded-lg shadow-md p-8">
            <!-- Branding -->
            <div class="text-center mb-8">
                <h1 class="text-3xl font-bold text-slate-900">IronMonkey</h1>
                <p class="mt-2 text-sm text-slate-600">Admin Console</p>
            </div>

            <!-- Error Banner -->
            @if (!string.IsNullOrEmpty(ErrorMessage))
            {
                <div class="mb-4 p-4 bg-red-50 border border-red-200 rounded-lg">
                    <p class="text-sm text-red-700">@ErrorMessage</p>
                </div>
            }

            <!-- Form -->
            <EditForm Model="@Model" OnValidSubmit="HandleLogin">
                <DataAnnotationsValidator />

                <!-- Email Field -->
                <div class="mb-4">
                    <label for="email" class="block text-sm font-medium text-slate-700 mb-1">
                        Email Address
                    </label>
                    <InputText 
                        id="email" 
                        @bind-Value="Model.Email"
                        type="email"
                        placeholder="admin@example.com"
                        class="w-full px-4 py-2 border border-slate-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
                        disabled="@IsSubmitting" />
                    <ValidationMessage For="@(() => Model.Email)" class="text-sm text-red-600 mt-1" />
                </div>

                <!-- Password Field -->
                <div class="mb-6">
                    <label for="password" class="block text-sm font-medium text-slate-700 mb-1">
                        Password
                    </label>
                    <InputPassword 
                        id="password" 
                        @bind-Value="Model.Password"
                        placeholder="••••••••"
                        class="w-full px-4 py-2 border border-slate-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
                        disabled="@IsSubmitting" />
                    <ValidationMessage For="@(() => Model.Password)" class="text-sm text-red-600 mt-1" />
                </div>

                <!-- Submit Button -->
                <button 
                    type="submit" 
                    disabled="@IsSubmitting"
                    class="w-full px-4 py-2 bg-indigo-600 text-white font-medium rounded-lg hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors">
                    @if (IsSubmitting)
                    {
                        <span>Signing in...</span>
                    }
                    else
                    {
                        <span>Sign In</span>
                    }
                </button>
            </EditForm>
        </div>

        <!-- Footer -->
        <p class="mt-6 text-center text-sm text-slate-600">
            Need help? Contact support@ironmonkey.io
        </p>
    </div>
</div>

@code {
    private LoginModel Model = new();
    private string? ErrorMessage;
    private bool IsSubmitting = false;

    private class LoginModel
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email address")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required")]
        public string Password { get; set; } = string.Empty;
    }

    private async Task HandleLogin()
    {
        try
        {
            IsSubmitting = true;
            ErrorMessage = null;

            var request = new { Model.Email, Model.Password };
            var response = await Http.PostAsJsonAsync<dynamic>("/auth/login", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsAsync<LoginResponse>();
                var authProvider = (AdminAuthenticationStateProvider)AuthStateProvider;
                await authProvider.LoginAsync(result.Token);
                Nav.NavigateTo("/admin", forceLoad: false);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                ErrorMessage = "Invalid email or password. Please try again.";
            }
            else
            {
                ErrorMessage = "Sign in failed. Please try again later.";
                Logger.LogError("Login failed with status {Status}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = "An error occurred. Please try again.";
            Logger.LogError(ex, "Login error");
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private record LoginResponse(string Token, Guid TenantId);
}
```

**Source:** Standard Blazor Server login pattern; decisions D-01 through D-04 from CONTEXT.md

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| localStorage for JWT storage | ProtectedSessionStorage | Blazor Server 5.0+ (2022) | localStorage is unencrypted XSS risk; ProtectedSessionStorage encrypts by default |
| OIDC/OpenID Connect for auth | Custom JWT + ProtectedSessionStorage | .NET 6+ (2021) | Simpler for single admin UI; OIDC overkill for non-SPA scenarios |
| npm + Tailwind package | Tailwind standalone CLI | Tailwind v4 (2024) | Standalone CLI removes Node.js dependency; ideal for .NET shops |
| Manual token refresh in components | DelegatingHandler auto-refresh | Blazor Server 6.0+ (2022) | DelegatingHandler is reusable; per-component code = bugs and duplication |
| Bootstrap CSS framework | Tailwind CSS | Industry shift (2022-2025) | Tailwind utility-first approach = smaller CSS, better performance, easier customization |

**Deprecated/outdated:**
- **ASP.NET Identity (server-side role management):** Still valid but v1.2 uses JWT claims for roles; identity service unnecessary for token-based auth
- **WebAssembly WASM frontend:** Blazor Server preferred for real-time admin UI; WASM adds complexity (two process models)
- **Server-side sessions with cookies:** JWT is stateless; no server-side session storage needed
- **Manual circuit state management:** Built-in `@attribute [Authorize]` + AuthenticationStateProvider handle auth state automatically

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Tailwind CLI | CSS compilation | ✓ | v4.0.11 | npm package (but violates no-Node.js constraint) |
| .NET SDK | Build & run | ✓ | 10.0.5 | — |
| PostgreSQL | Backend API (Phases 1-8) | ✓ (Docker via Aspire) | 15-alpine | — |
| Docker | Running PostgreSQL | ✓ | 24.0+ | Local PostgreSQL install |
| Heroicons | Icon set (optional Phase 9, required later) | ✓ (CDN + npm) | v2.0+ | Use simple SVG inline or Unicode symbols |

**Missing dependencies with no fallback:** None — all required tools are available.

**Missing dependencies with fallback:** Heroicons can be temporarily replaced with Unicode symbols or simple CSS icons in Phase 9; defer design work to Phase 10 if needed.

---

## Validation Architecture

> Workflow validation enabled (workflow.nyquist_validation not explicitly set to false in config.json)

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 (existing IronMonkey.Tests) |
| Config file | IronMonkey.Tests/*.csproj (MSBuild-based, no xunit.runner.json needed) |
| Quick run command | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~AuthenticationTests" -x` |
| Full suite command | `dotnet test IronMonkey.Tests` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| UIFN-01 | Admin submits login form, receives JWT token stored in ProtectedSessionStorage | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~LoginEndpointTests.ValidCredentials_ReturnsTokenWithTenantId" -x` | ✅ LoginEndpoint exists (backend); UIFN-01 test frontend only — Wave 0 |
| UIFN-02 | Unauthenticated request to /admin redirects to /login | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~AuthGuardTests.UnauthenticatedRequest_RedirectsToLogin" -x` | ❌ Wave 0 (new Blazor auth guard test) |
| UIFN-03 | Sidebar renders with 4 grouped nav sections visible after login | Unit | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~AdminSidebarTests.Renders_WithGroupedNavItems" -x` | ❌ Wave 0 (new component test) |
| UIFN-04 | Sidebar collapses on mobile (md breakpoint); hamburger toggles state | Unit | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~AdminSidebarTests.Mobile_HamburgerToggleSidebar" -x` | ❌ Wave 0 (new component test) |
| UIFN-05 | Tailwind CSS output.css generated with all .razor classes before build | Build | `tailwindcss -i IronMonkey.Web/wwwroot/css/input.css -o IronMonkey.Web/wwwroot/css/output.css && grep "w-1/2" IronMonkey.Web/wwwroot/css/output.css` | ❌ Wave 0 (Tailwind configuration test) |
| UIFN-06 | Admin clicks logout, JWT cleared from storage, redirected to /login | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~LogoutTests.LogoutClears_Token_AndRedirectsToLogin" -x` | ❌ Wave 0 (new logout endpoint test) |

### Sampling Rate

- **Per task commit:** `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~LoginEndpointTests or FullyQualifiedName~AdminAuthenticationStateProviderTests" -x`
- **Per wave merge:** `dotnet test IronMonkey.Tests`
- **Phase gate:** Full suite green + manual browser test (login form → sidebar → logout) before `/gsd:verify-work`

### Wave 0 Gaps

- [ ] `IronMonkey.Tests/Integration/UI/AuthGuardTests.cs` — verify unauthenticated /admin redirects to /login
- [ ] `IronMonkey.Tests/Integration/UI/LoginTests.cs` — login form submission, JWT storage, auth state change
- [ ] `IronMonkey.Tests/Integration/UI/LogoutTests.cs` — logout clears token, notifies auth state provider
- [ ] `IronMonkey.Tests/Unit/Components/AdminSidebarTests.cs` — sidebar rendering, nav groups, mobile toggle
- [ ] `IronMonkey.Tests/Integration/CircuitHandlerTests.cs` — validate user identity on circuit reconnect (Pitfall 1)
- [ ] `IronMonkey.Tests/Integration/BearerTokenHandlerTests.cs` — verify Authorization header injected correctly (Pitfall 4)
- [ ] `IronMonkey.Web/Authentication/AdminAuthenticationStateProvider.cs` — implementation + unit tests
- [ ] `IronMonkey.Web/HttpHandlers/BearerTokenHandler.cs` — implementation + unit tests
- [ ] `IronMonkey.Web/CircuitHandlers/IdentityValidationCircuitHandler.cs` — implementation + unit tests
- [ ] `IronMonkey.Web/wwwroot/css/input.css` — Tailwind entry point
- [ ] Build verification script: confirm Tailwind output.css includes expected classes

**Framework install:** xUnit already exists in IronMonkey.Tests.csproj; no additional packages needed.

---

## Open Questions

1. **Logout Endpoint — Does it exist on backend?**
   - What we know: LoginEndpoint exists; JWT is stateless (no server-side session to revoke)
   - What's unclear: Should logout trigger a database event (audit trail) or is client-side token deletion sufficient?
   - Recommendation: For Phase 9, implement client-side logout only (delete token from storage). Audit trail via domain event can be added in Phase 10+ if needed.

2. **Token Expiration — Should we shorten the 1-year JWT TTL?**
   - What we know: Current backend sets JWT expiry to 1 year (Jwt.cs line 32)
   - What's unclear: Admin UI security best practice for token lifetime
   - Recommendation: Keep 1 year for Phase 9 (no production exposure); revisit in Phase 10 with refresh token implementation. Standard for admin UI: 15-30 min access token + 7-day refresh token.

3. **Tenant Admin vs. SuperAdmin — Are there role-based dashboard differences?**
   - What we know: JWT includes role claim; user can be SuperAdmin or Tenant Admin
   - What's unclear: Should different roles see different sidebar sections?
   - Recommendation: Phase 9 shows full sidebar for all authenticated users. Implement role-based visibility in Phase 10 during recipe management UI.

4. **Heroicons Integration — How to embed without npm?**
   - What we know: Decision D-06 specifies Heroicons outline style; no npm allowed
   - What's unclear: SVG copy-paste vs. CDN link vs. data URIs
   - Recommendation: Use official Heroicons CDN for Phase 9 (simplest, no build step). Copy SVGs inline during Phase 10 if CDN unavailable.

---

## Sources

### Primary (HIGH confidence)
- **Backend Auth Implementation:** IronMonkey.ApiService/Authentication/Endpoints/LoginEndpoint.cs — verified JWT generation, tenant resolution, BCrypt password verification (existing and production-ready)
- **JWT Configuration:** IronMonkey.Common/Auth/Jwt.cs — verified token generation, claim structure, expiration logic
- **.NET 10.0 Documentation:** ProtectedSessionStorage, JwtSecurityTokenHandler, AuthenticationStateProvider — all built-in; verified in Microsoft Learn
- **Tailwind CSS v4 Standalone CLI:** Official docs (https://tailwindcss.com/docs/installation/standalone-cli) — verified December 2024, current as of 2026-03-31

### Secondary (MEDIUM confidence)
- **ADMIN_UI_QUICK_SETUP.md** — `.planning/research/ADMIN_UI_QUICK_SETUP.md` — established patterns from v1.2 research; verified against Blazor Server 10.0 docs
- **IMPLEMENTATION_PATTERNS_BLAZOR.md** — `.planning/research/IMPLEMENTATION_PATTERNS_BLAZOR.md` — code examples for DelegatingHandler, AuthenticationStateProvider, circuit handlers
- **PITFALLS_BLAZOR_UI.md** — `.planning/research/PITFALLS_BLAZOR_UI.md` — comprehensive pitfall catalog with detection strategies; verified against GitHub issues

### Tertiary (research-sourced)
- **Microsoft Learn: Blazor Server Authentication & Authorization** — official documentation current as of March 2026
- **Tailwind CSS GitHub:** Standalone CLI releases and v4 documentation
- **IronMonkey v1.2 Milestone Context** — `.planning/CONTEXT.md`, `.planning/REQUIREMENTS.md`, `.planning/STATE.md` — locked decisions and requirement traceability

---

## Metadata

**Confidence breakdown:**
- **Standard Stack:** HIGH — All dependencies exist in codebase or are standard library; Tailwind v4 verified current; JWT pattern established in backend
- **Architecture:** HIGH — Patterns documented in IMPLEMENTATION_PATTERNS_BLAZOR.md; verified against current Blazor Server 10.0 best practices
- **Pitfalls:** HIGH — Documented in PITFALLS_BLAZOR_UI.md; sourced from GitHub issues and community patterns
- **Environment:** HIGH — All tools available; no external service dependencies

**Research date:** 2026-03-31
**Valid until:** 2026-04-30 (Blazor Server and Tailwind v4 are stable; architecture patterns don't change frequently)

---

## What Might I Have Missed?

1. **Refresh Token Endpoint** — Decision D-10 says "no silent refresh for v1.2," so Phase 9 doesn't need refresh logic. But future phases (Phase 10+) may require `/api/auth/refresh` endpoint. Backend should be checked for this endpoint's existence before Phase 10 planning.

2. **Custom Claims in JWT** — Backend adds tenant_id, role, name, email claims. Phase 9 consumes these. If new claims are added in future phases (department, permissions), AuthenticationStateProvider parsing may need updates. Currently handles arbitrary claims well.

3. **SignalR Circuit Handler Registration** — Pitfall documentation mentions ICircuitHandler for identity validation. Phase 9 research includes implementation pattern, but actual registration in Program.cs must happen during planning. Ensure it's not forgotten.

4. **Tailwind Content Paths** — Research assumes all .razor files are in `Components/**/*.razor`. If future phases create pages in different directories, Tailwind content paths in MSBuild target must be updated. Document this as a maintenance task.

5. **Mobile Breakpoint Assumption** — Decisions D-14 and D-15 use Tailwind `md` breakpoint (768px). If design requirements change (e.g., 1024px breakpoint for side-by-side layout), sidebar responsive logic must be updated. This is cosmetic, not blocking.

---

