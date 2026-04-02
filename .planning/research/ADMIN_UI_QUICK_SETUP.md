# Admin UI Stack — Quick Setup Reference

**Project:** IronMonkey v1.2 Admin UI
**For:** Phase 1 implementation kickoff

---

## One-Time Setup (Before Any Code)

### 1. Install Tailwind Standalone Binary

**Windows (winget):**
```bash
winget install TailwindLabs.TailwindCSS
```

**macOS (Homebrew):**
```bash
brew install tailwindcss
```

**Any OS (Manual):**
1. Visit: https://github.com/tailwindlabs/tailwindcss/releases
2. Download `tailwindcss-<os>-<arch>` (e.g., `tailwindcss-windows-x64.exe`)
3. Place in project root: `IronMonkey.Web/tailwindcss`
4. Verify: `tailwindcss --version`

### 2. Create CSS Entry Point

**File:** `IronMonkey.Web/wwwroot/css/input.css`

```css
@import "tailwindcss";
```

**That's it.** Tailwind v4 auto-detects .razor files.

---

## Code Changes Required

### 1. Create JwtAuthenticationStateProvider

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
        if (_initialized && _cachedPrincipal != null)
            return new AuthenticationState(_cachedPrincipal);

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri("https+http://apiservice");
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

        _cachedPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
        _initialized = true;
        return new AuthenticationState(_cachedPrincipal);
    }

    private static IEnumerable<Claim> ParseClaimsFromResponse(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            var claims = new List<Claim>();

            if (root.TryGetProperty("userId", out var userId))
                claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.GetString() ?? ""));

            if (root.TryGetProperty("email", out var email))
                claims.Add(new Claim(ClaimTypes.Email, email.GetString() ?? ""));

            if (root.TryGetProperty("tenantId", out var tenantId))
                claims.Add(new Claim("tenant_id", tenantId.GetString() ?? ""));

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

### 2. Update Program.cs

**File:** `IronMonkey.Web/Program.cs`

Add after `builder.AddServiceDefaults();`:

```csharp
// Authentication
builder.Services.AddScoped<AuthenticationStateProvider, JwtAuthenticationStateProvider>();
builder.Services.AddCascadingAuthenticationState();
```

### 3. Extend ApiClient

**File:** `IronMonkey.Web/ApiClient.cs`

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

    public async Task<List<TenantResponse>> GetTenantsAsync()
        => await httpClient.GetFromJsonAsync<List<TenantResponse>>("/api/admin/tenants") ?? [];

    public async Task<TenantResponse?> GetTenantAsync(Guid tenantId)
        => await httpClient.GetFromJsonAsync<TenantResponse>($"/api/admin/tenants/{tenantId}");

    // Response DTOs
    public record RecipeResponse(Guid Id, string Industry, string DisplayName, string Description);
    public record TenantResponse(Guid Id, string Name, string Status, DateTime CreatedAt);
}
```

### 4. Update IronMonkey.Web.csproj

**File:** `IronMonkey.Web/IronMonkey.Web.csproj`

Add before `</Project>`:

```xml
<Target Name="TailwindBuild" BeforeTargets="Build">
  <Exec Command="tailwindcss -i wwwroot/css/input.css -o wwwroot/css/output.css --minify" />
</Target>
```

### 5. Update App.razor

**File:** `IronMonkey.Web/Components/App.razor`

Replace:
```html
<link rel="stylesheet" href="@Assets["lib/bootstrap/dist/css/bootstrap.min.css"]" />
<link rel="stylesheet" href="@Assets["app.css"]" />
```

With:
```html
<link rel="stylesheet" href="css/output.css" />
```

---

## Create First Admin Page

**File:** `IronMonkey.Web/Components/Pages/Admin/Recipes.razor`

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
                                </tr>
                            </thead>
                            <tbody class="divide-y divide-gray-200">
                                @foreach (var recipe in recipes)
                                {
                                    <tr>
                                        <td class="px-6 py-4 text-sm text-gray-900">@recipe.Industry</td>
                                        <td class="px-6 py-4 text-sm text-gray-900">@recipe.DisplayName</td>
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

---

## Development Workflow

**Terminal 1: Run Blazor app**
```bash
cd IronMonkey.Web
dotnet watch run
```

**Terminal 2: Watch Tailwind**
```bash
cd IronMonkey.Web
tailwindcss -i wwwroot/css/input.css -o wwwroot/css/output.css --watch
```

---

## Testing

1. **Navigate to:** `https://localhost:7000/admin/recipes` (or configured HTTPS port)
2. **Expected behavior:**
   - If authenticated as SuperAdmin: See recipe table
   - If authenticated as non-admin: See "You do not have access"
   - If not authenticated: Redirect to login (implementation detail for Phase 1)
3. **CSS should be applied:** Table has striped rows, Tailwind gray styling

---

## Files Created/Modified Summary

| File | Action | What's Needed |
|------|--------|--------------|
| `Authentication/JwtAuthenticationStateProvider.cs` | Create | Copy class code above |
| `Program.cs` | Modify | Add auth service registration |
| `ApiClient.cs` | Modify | Add typed endpoint methods |
| `IronMonkey.Web.csproj` | Modify | Add MSBuild Tailwind target |
| `Components/App.razor` | Modify | Replace Bootstrap with Tailwind link |
| `Components/Pages/Admin/Recipes.razor` | Create | Copy page code above |
| `wwwroot/css/input.css` | Create | Add `@import "tailwindcss";` |
| `wwwroot/css/output.css` | Generate | Auto-generated during build |

---

## Checklist Before First Build

- [ ] Tailwind CLI installed (`tailwindcss --version` works)
- [ ] `wwwroot/css/input.css` exists
- [ ] `Program.cs` registers `JwtAuthenticationStateProvider`
- [ ] `ApiClient.cs` has typed methods
- [ ] `.csproj` has Tailwind MSBuild target
- [ ] `App.razor` links `css/output.css`
- [ ] Admin page created with `@attribute [Authorize]`
- [ ] `/api/auth/me` endpoint exists on backend
- [ ] `/api/admin/recipes` endpoint exists on backend

---

## No Additional NuGet Packages Needed

Everything ships with .NET 10.0:
- ✓ System.Net.Http (HttpClient, IHttpClientFactory)
- ✓ Microsoft.AspNetCore.Components (Razor components)
- ✓ Microsoft.AspNetCore.Components.Authorization (AuthenticationStateProvider)
- ✓ System.Security.Claims (ClaimsPrincipal)
- ✓ System.Text.Json (JSON parsing)

---

## Next Steps (For Phase Planning)

1. **Phase 1a (Setup):** Configure this stack
2. **Phase 1b (Login):** Create login page → API endpoint
3. **Phase 2:** Recipe management (list, create, edit)
4. **Phase 3:** Tenant management
5. **Phase 4+:** Other admin workflows

**Total setup time:** 1-2 hours of configuration before feature work begins.
