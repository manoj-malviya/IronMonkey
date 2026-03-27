# Stack Research: Blazor Server Admin UI with Tailwind CSS

**Domain:** Blazor Server admin UI with CSS styling
**Researched:** 2026-03-27
**Confidence:** HIGH
**Milestone:** v1.2 Admin UI (subsequent milestone)

## Executive Summary

The Blazor Server admin UI requires **THREE configuration additions and ONE styling tool** — no NuGet packages needed:

1. **Typed HttpClient service** — Extend existing `ApiClient` to call backend Minimal API endpoints
2. **Custom AuthenticationStateProvider** — Parse JWT tokens from API, provide claims to Blazor components
3. **Role-based authorization guards** — Use built-in `[Authorize]` attributes and `AuthorizeView` for admin pages
4. **Tailwind CSS v4 standalone CLI** — Eliminate npm dependency; auto-detects .razor files

The existing `IronMonkey.Web` project is already scaffolded as Blazor Server with .NET 10.0 and Aspire service defaults. The work is **configuration and integration**, not new packages.

---

## Recommended Stack Additions

### API Communication (No New Packages)

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| IHttpClientFactory | 10.0.5 (framework) | Pool HTTP connections, typed HttpClient pattern | Built-in to ASP.NET Core 10.0; prevents socket exhaustion as admin UI scales |
| Microsoft.Extensions.ServiceDiscovery | 10.1.0 (existing) | Resolve `https+http://apiservice` at runtime | Already in IronMonkey.ServiceDefaults; Aspire injects endpoints via env vars |
| Microsoft.Extensions.Http.Resilience | 10.1.0 (existing) | Automatic retry + circuit breaker | Already in ServiceDefaults; prevents cascading failures from API service downtime |

**Enhancement to existing setup:** Expand `ApiClient` from empty stub to typed methods for admin endpoints (recipes, tenants, users, roles, etc.).

### Authentication & Authorization (No New Packages)

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| System.Security.Claims | 10.0 (framework) | ClaimsPrincipal, identity model | Native to ASP.NET Core; IronMonkey API already issues JWT with `tenant_id`, `user_id`, `roles` claims |
| AuthenticationStateProvider | 10.0.5 (framework) | Blazor auth state management | Provides `GetAuthenticationStateAsync()`; cascades via CascadingAuthenticationState to all components |
| [Authorize] attribute | 10.0.5 (framework) | Declarative page-level authorization | Enforced server-side on Blazor Server (not bypassable like client-side checks) |
| AuthorizeView component | 10.0.5 (framework) | Conditional UI rendering by role | Lightweight; renders different UI for authenticated/unauthorized/different roles |

**Implementation required:** Custom `JwtAuthenticationStateProvider` class to fetch user claims from API and cache for circuit lifetime.

### Styling

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| Tailwind CSS | 4.x (standalone binary) | Utility-first CSS framework | Removes npm dependency entirely; v4 standalone auto-detects .razor files without config |
| Tailwind standalone CLI | 4.x | Command-line CSS generator | Download from GitHub releases; runs as single binary; integrates with MSBuild for automated builds |

**Alternative approach considered:** Tailwind with npm + PostCSS (requires Node.js in build pipeline — rejected per PROJECT.md constraint to avoid Node.js dependency).

### Component & Layout Patterns (No New Libraries for MVP)

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| Razor Components | 10.0.5 (framework) | Native component model | Blazor's built-in component system; parameters, cascading parameters, event callbacks |
| @page routing | 10.0.5 (framework) | Page routing with role guards | Built-in; supports `@attribute [Authorize(Roles = "Admin")]` for route protection |
| MainLayout.razor | (existing) | App shell & sidebar | Already scaffolded; provides consistent header, sidebar, footer across admin pages |

**MVP scope:** Do NOT add component libraries (MudBlazor, Syncfusion, etc.) unless admin UI specifically requires rich DataGrids with server-side pagination. Use native Razor + Tailwind for CRUD pages.

---

## Installation & Configuration

### 1. Enhance ApiClient Service (Existing File)

**File:** `IronMonkey.Web/ApiClient.cs`

Current (stub):
```csharp
public class ApiClient(HttpClient httpClient) { }
```

Extend to provide typed methods:
```csharp
namespace IronMonkey.Web;

using System.Net.Http.Json;

public class ApiClient(HttpClient httpClient)
{
    // Admin endpoints
    public async Task<List<RecipeResponse>> GetRecipesAsync()
        => await httpClient.GetFromJsonAsync<List<RecipeResponse>>("/api/admin/recipes") ?? [];

    public async Task<RecipeResponse?> GetRecipeAsync(Guid id)
        => await httpClient.GetFromJsonAsync<RecipeResponse>($"/api/admin/recipes/{id}");

    public async Task<TenantResponse?> GetTenantAsync(Guid tenantId)
        => await httpClient.GetFromJsonAsync<TenantResponse>($"/api/admin/tenants/{tenantId}");

    public async Task<List<TenantResponse>> GetTenantsAsync()
        => await httpClient.GetFromJsonAsync<List<TenantResponse>>("/api/admin/tenants") ?? [];

    public async Task<HttpResponseMessage> CreateRecipeAsync(CreateRecipeRequest request)
        => await httpClient.PostAsJsonAsync("/api/admin/recipes", request);

    // Auth endpoint for getting current user claims
    public async Task<string?> GetCurrentUserAsync()
        => await httpClient.GetStringAsync("/api/auth/me");

    // Define response DTOs
    public record RecipeResponse(Guid Id, string Industry, string DisplayName);
    public record TenantResponse(Guid Id, string Name, string Status);
    public record CreateRecipeRequest(string Industry, string DisplayName, string Description);
}
```

**Why:** Typed methods prevent typos, enable IntelliSense, centralize API contract. HttpClient pooling via IHttpClientFactory (already configured in Program.cs) prevents socket exhaustion.

### 2. Create Custom AuthenticationStateProvider (New Class)

**File:** `IronMonkey.Web/Authentication/JwtAuthenticationStateProvider.cs`

```csharp
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace IronMonkey.Web.Authentication;

public class JwtAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private ClaimsPrincipal? _cachedPrincipal;
    private bool _initialized = false;

    public JwtAuthenticationStateProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // Blazor Server: circuit lifetime == session lifetime
        // Cache the principal for the circuit's lifetime
        if (_initialized && _cachedPrincipal != null)
            return new AuthenticationState(_cachedPrincipal);

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri("https+http://apiservice");

            // Fetch current user from API
            // The API validates the JWT and returns claims
            var response = await httpClient.GetAsync("/api/auth/me");

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var claims = ParseClaimsFromResponse(json);
                _cachedPrincipal = new ClaimsPrincipal(
                    new ClaimsIdentity(claims, "jwt")
                );
                _initialized = true;
                return new AuthenticationState(_cachedPrincipal);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Auth state fetch failed: {ex.Message}");
        }

        // Unauthenticated principal
        _cachedPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
        _initialized = true;
        return new AuthenticationState(_cachedPrincipal);
    }

    private static IEnumerable<Claim> ParseClaimsFromResponse(string responseJson)
    {
        // API returns JSON with user claims
        // Example: { "userId": "xxx", "tenantId": "yyy", "roles": ["Admin", "SuperAdmin"] }
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            var claims = new List<Claim>();

            // Parse standard claims
            if (root.TryGetProperty("userId", out var userId))
                claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.GetString() ?? ""));

            if (root.TryGetProperty("email", out var email))
                claims.Add(new Claim(ClaimTypes.Email, email.GetString() ?? ""));

            if (root.TryGetProperty("tenantId", out var tenantId))
                claims.Add(new Claim("tenant_id", tenantId.GetString() ?? ""));

            // Parse roles array
            if (root.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in roles.EnumerateArray())
                {
                    if (role.GetString() is string roleName)
                        claims.Add(new Claim(ClaimTypes.Role, roleName));
                }
            }

            return claims;
        }
        catch
        {
            return Enumerable.Empty<Claim>();
        }
    }
}
```

**Register in Program.cs:**

```csharp
// After builder.AddServiceDefaults();

builder.Services.AddScoped<AuthenticationStateProvider, JwtAuthenticationStateProvider>();
builder.Services.AddCascadingAuthenticationState();
```

**Why:**
- Blazor Server stores auth state in SignalR circuit (server-side)
- Circuit lifetime = session lifetime; no token storage on client
- Caching prevents repeated API calls during component initialization
- CascadingAuthenticationState makes auth available to all child components

### 3. Create Protected Admin Pages

**File:** `IronMonkey.Web/Components/Pages/Admin/RecipeManagement.razor`

```razor
@page "/admin/recipes"
@using Microsoft.AspNetCore.Authorization
@attribute [Authorize(Roles = "SuperAdmin")]
@inject ApiClient ApiClient

<PageTitle>Recipe Management</PageTitle>

<div class="min-h-screen bg-gray-50">
    <div class="px-8 py-6">
        <h1 class="text-3xl font-bold text-gray-900">Recipe Management</h1>
    </div>

    <AuthorizeView Roles="SuperAdmin">
        <Authorized>
            <div class="px-8 py-4">
                <p class="text-gray-700">Hello, @context.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value</p>

                @if (recipes == null)
                {
                    <p class="text-gray-500">Loading recipes...</p>
                }
                else if (recipes.Count == 0)
                {
                    <p class="text-gray-500">No recipes found.</p>
                }
                else
                {
                    <div class="mt-6 overflow-x-auto shadow ring-1 ring-black ring-opacity-5 rounded-lg">
                        <table class="min-w-full divide-y divide-gray-300">
                            <thead class="bg-gray-100">
                                <tr>
                                    <th scope="col" class="px-6 py-3 text-left text-sm font-semibold text-gray-900">Industry</th>
                                    <th scope="col" class="px-6 py-3 text-left text-sm font-semibold text-gray-900">Display Name</th>
                                    <th scope="col" class="relative px-6 py-3">
                                        <span class="sr-only">Actions</span>
                                    </th>
                                </tr>
                            </thead>
                            <tbody class="divide-y divide-gray-200">
                                @foreach (var recipe in recipes)
                                {
                                    <tr>
                                        <td class="px-6 py-4 text-sm text-gray-900">@recipe.Industry</td>
                                        <td class="px-6 py-4 text-sm text-gray-900">@recipe.DisplayName</td>
                                        <td class="px-6 py-4 text-right text-sm font-medium">
                                            <a href="#" class="text-indigo-600 hover:text-indigo-900">Edit</a>
                                        </td>
                                    </tr>
                                }
                            </tbody>
                        </table>
                    </div>
                }
            </div>
        </Authorized>
        <NotAuthorized>
            <div class="px-8 py-4">
                <p class="text-red-600">You do not have access to this page.</p>
            </div>
        </NotAuthorized>
    </AuthorizeView>
</div>

@code {
    private List<ApiClient.RecipeResponse>? recipes;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            recipes = await ApiClient.GetRecipesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load recipes: {ex.Message}");
            recipes = new();
        }
    }
}
```

**Key patterns:**
- `@attribute [Authorize(Roles = "SuperAdmin")]` — Enforce at page level (server-side)
- `<AuthorizeView Roles="SuperAdmin">` — Conditional UI rendering (supplementary)
- Tailwind utility classes for styling (no Bootstrap)
- Typed methods from ApiClient for data fetching

### 4. Tailwind CSS Standalone Setup

#### Step 1: Download Tailwind Standalone Binary

```bash
# Windows (via winget)
winget install TailwindLabs.TailwindCSS

# macOS (via Homebrew)
brew install tailwindcss

# Or download manually from:
# https://github.com/tailwindlabs/tailwindcss/releases
# Place in project root or /Tools directory
```

Verify installation:
```bash
tailwindcss --version
```

#### Step 2: Create CSS Entry Point

**File:** `IronMonkey.Web/wwwroot/css/input.css`

```css
@import "tailwindcss";
```

That's it. Tailwind v4 **automatically detects .razor files** in the project without needing a config file.

#### Step 3: Update App.razor

**File:** `IronMonkey.Web/Components/App.razor`

Replace Bootstrap reference with Tailwind output:

```html
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />

    <!-- Tailwind CSS output -->
    <link rel="stylesheet" href="css/output.css" />
    <link rel="stylesheet" href="IronMonkey.Web.styles.css" />

    <ImportMap />
    <link rel="icon" type="image/png" href="favicon.png" />
    <HeadOutlet />
</head>

<body>
    <Routes />
    <script src="_framework/blazor.web.js"></script>
</body>

</html>
```

#### Step 4: Add MSBuild Target to .csproj

**File:** `IronMonkey.Web/IronMonkey.Web.csproj`

Add before closing `</Project>`:

```xml
<Target Name="TailwindBuild" BeforeTargets="Build">
  <Exec Command="tailwindcss -i wwwroot/css/input.css -o wwwroot/css/output.css --minify" />
</Target>
```

This runs during `dotnet build` and `dotnet publish` automatically.

#### Step 5: Development Workflow

**Terminal 1: Run Blazor app with watch mode**
```bash
cd IronMonkey.Web
dotnet watch run
```

**Terminal 2: Watch for CSS changes**
```bash
cd IronMonkey.Web
tailwindcss -i wwwroot/css/input.css -o wwwroot/css/output.css --watch
```

As you edit .razor files, Tailwind automatically detects new classes and regenerates `output.css`. Blazor's hot reload picks up the changes.

### 5. Update MainLayout for Admin Navigation

**File:** `IronMonkey.Web/Components/Layout/MainLayout.razor`

Add sidebar navigation with role guards:

```razor
@inherits LayoutComponentBase

<div class="flex h-screen bg-gray-100">
    <!-- Sidebar -->
    <div class="w-64 bg-gray-800 shadow-lg">
        <div class="px-4 py-6">
            <h1 class="text-xl font-bold text-white">IronMonkey</h1>
        </div>

        <nav class="mt-8">
            <NavLink href="/admin/recipes" class="block px-4 py-2 text-gray-300 hover:bg-gray-700 hover:text-white">
                Recipes
            </NavLink>
            <NavLink href="/admin/tenants" class="block px-4 py-2 text-gray-300 hover:bg-gray-700 hover:text-white">
                Tenants
            </NavLink>
            <NavLink href="/admin/users" class="block px-4 py-2 text-gray-300 hover:bg-gray-700 hover:text-white">
                Users
            </NavLink>
        </nav>
    </div>

    <!-- Main content -->
    <div class="flex-1 overflow-auto">
        <main class="max-w-7xl mx-auto">
            @Body
        </main>
    </div>
</div>

@code { }
```

---

## Integration with Existing Architecture

### API Communication Flow

```
Blazor Component (e.g., RecipeManagement.razor)
    ↓
ApiClient.GetRecipesAsync()
    ↓
HttpClient (via IHttpClientFactory)
    ↓
ServiceDiscovery resolves "https+http://apiservice"
    ↓
Aspire env vars: Services__apiservice__https = actual URL
    ↓
IronMonkey.ApiService (Minimal API endpoints)
    ↓
EF Core → PostgreSQL
```

**Key point:** Aspire's service discovery middleware (already in ServiceDefaults) handles the URL resolution at runtime. No hardcoding of localhost:5000.

### Authentication Flow

```
Admin visits /admin/recipes
    ↓
Blazor checks [Authorize] attribute
    ↓
JwtAuthenticationStateProvider.GetAuthenticationStateAsync()
    ↓
ApiClient calls /api/auth/me
    ↓
API service extracts JWT from Authorization header
    ↓
API validates and returns claims: { userId, tenantId, roles }
    ↓
Blazor builds ClaimsPrincipal from claims
    ↓
[Authorize(Roles = "SuperAdmin")] check passes
    ↓
AuthorizeView conditionally renders content
```

**Critical:** Blazor Server auth happens on the SERVER. The JWT is validated server-side. Authorization checks cannot be bypassed from the browser.

### CSS Generation Flow

```
Developer edits MainLayout.razor
    ↓ (Terminal 2: tailwindcss --watch)
Tailwind detects .razor file change
    ↓
Scans all .razor files for Tailwind class names
    ↓
Generates output.css with only used classes
    ↓ (Terminal 1: dotnet watch)
Blazor detects CSS change
    ↓
Browser live-reload applies new styles
    ↓
Admin sees changes immediately
```

**Benefit:** Zero build tooling overhead. Pure .NET + single binary. No npm, no webpack, no PostCSS.

---

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| Component libraries (MudBlazor, Syncfusion) in MVP | Overkill for CRUD pages; adds bundle size and startup time | Native Razor components + Tailwind CSS |
| Storing JWT in localStorage (Blazor Server) | Blazor Server is server-rendered; tokens belong server-side; no client-side storage needed | Trust ASP.NET Core's session management via SignalR circuit |
| ProtectedLocalStorage for JWT (Blazor Server) | Unnecessary complexity; Blazor Server auth is built-in | Use AuthenticationStateProvider to integrate with ASP.NET Core auth |
| npm + Tailwind CLI (with Node.js) | Adds build pipeline complexity; conflicts with .NET-only toolchain | Tailwind v4 standalone binary (no Node.js) |
| Bootstrap CSS + Tailwind CSS together | Conflicting utility class names; larger bundle size | Tailwind CSS only; replace Bootstrap |
| Client-side authorization checks only | Bypassable from browser dev tools; not secure | [Authorize] attribute on @page enforces server-side |

---

## Alternatives Considered

| Recommended | Alternative | Why Not |
|-------------|-------------|---------|
| Tailwind v4 standalone | Tailwind + npm + PostCSS | Adds Node.js dependency; slower rebuild; complicates CI/CD |
| Native Razor components | MudBlazor DataGrid | MVP doesn't need advanced components; adds 500KB+ to bundle |
| Custom JwtAuthenticationStateProvider | ASP.NET Core Identity scaffolding | IronMonkey uses JWT from custom API, not Identity; different auth model |
| ApiClient typed methods | Direct HttpClient.GetFromJsonAsync in components | Centralizes API contract; prevents typos; enables easy refactoring |
| MSBuild Tailwind target | Manual tailwindcss CLI before each build | Automated; can't forget; integrates with CI/CD publish pipeline |

---

## Development Setup Checklist

- [ ] Install Tailwind standalone: `winget install TailwindLabs.TailwindCSS` (or brew/manual)
- [ ] Create `wwwroot/css/input.css` with `@import "tailwindcss";`
- [ ] Create `wwwroot/css/.gitkeep` (output.css is generated)
- [ ] Update `Components/App.razor` to reference `css/output.css`
- [ ] Add `<Target Name="TailwindBuild" ...>` to `IronMonkey.Web.csproj`
- [ ] Create `Authentication/JwtAuthenticationStateProvider.cs`
- [ ] Register provider in `Program.cs`: `builder.Services.AddScoped<AuthenticationStateProvider, JwtAuthenticationStateProvider>();`
- [ ] Extend `ApiClient.cs` with typed admin endpoint methods
- [ ] Update `MainLayout.razor` with sidebar navigation
- [ ] Create `/admin/recipes` page as proof-of-concept
- [ ] Terminal 1: `dotnet watch run` (from IronMonkey.Web)
- [ ] Terminal 2: `tailwindcss -i wwwroot/css/input.css -o wwwroot/css/output.css --watch` (from IronMonkey.Web)
- [ ] Test: Navigate to `https://localhost:7000/admin/recipes` (or configured URL)
- [ ] Verify Tailwind styles applied (sidebar dark gray, table striped, etc.)

---

## No New NuGet Packages Required

All capabilities ship with .NET 10.0:
- HttpClient + IHttpClientFactory (System.Net.Http)
- AuthenticationStateProvider (Microsoft.AspNetCore.Components.Authorization)
- [Authorize] attribute (Microsoft.AspNetCore.Authorization)
- JSON parsing (System.Text.Json)

Existing packages already in IronMonkey.Web:
- Microsoft.Extensions.ServiceDiscovery (10.1.0) — from ServiceDefaults
- Microsoft.Extensions.Http.Resilience (10.1.0) — from ServiceDefaults

**No additions needed.**

---

## Version Compatibility

| Technology | Version | Compatibility Notes |
|-----------|---------|-------------------|
| .NET | 10.0 | Blazor Server runtime |
| ASP.NET Core | 10.0.5 | Web host, authentication, routing |
| Blazor Server | 10.0.5 (via framework) | UI component framework |
| AuthenticationStateProvider | 10.0.5 (via framework) | Built-in |
| Tailwind CSS (standalone) | 4.x | No dependencies; runs on Windows/macOS/Linux |
| IHttpClientFactory | 10.0.5 (via framework) | Built-in connection pooling |

**Critical:** Ensure all references to ASP.NET Core packages are exactly 10.0.5 to match IronMonkey.ApiService.

---

## Sources

- [Microsoft Learn: Call a web API from Blazor (.NET 10.0)](https://learn.microsoft.com/en-us/aspnet/core/blazor/call-web-api?view=aspnetcore-10.0) — HttpClient patterns, IHttpClientFactory, service discovery
- [Microsoft Learn: Blazor authentication and authorization (.NET 10.0)](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/?view=aspnetcore-10.0) — AuthenticationStateProvider, AuthorizeView, role-based authorization
- [DEV Community: Tailwind CSS v4 Standalone in Blazor (2026)](https://dev.to/cristiansifuentes/tailwind-css-v4-standalone-in-blazor-webassembly-a-clean-native-integration-for-the-net-26lk) — v4 standalone CLI setup, .razor file auto-detection
- [Steven Giesel: Tailwind v4 with Blazor (2025)](https://steven-giesel.com/blogPost/364c43d2-b31e-4377-8001-ac75ce78cdc6) — MSBuild target integration, development workflow
- [Blazorise: Blazor and Tailwind — Quick Setup Without npm](https://blazorise.com/blog/blazor-and-tailwind-quick-setup-without-npm) — npm-free Tailwind approach
- [.NET Aspire 13.1.0 Service Discovery](https://learn.microsoft.com/en-us/dotnet/aspire/service-discovery/overview) — Service discovery integration (Aspire 13.1.0 used in IronMonkey)
- [Medium: .NET Aspire Standalone — Blazor Server with Web API (2026)](https://medium.com/@pieter.artorius.vanzyl/net-aspire-standalone-blazor-server-with-web-api-77abd45803a0) — Service discovery with Blazor Server

---

**Stack research for:** Blazor Server admin UI with Tailwind CSS integration (IronMonkey v1.2)
**Researched:** 2026-03-27
**Overall confidence:** HIGH — All recommendations verified against .NET 10.0 official docs and current 2026 community guidance
