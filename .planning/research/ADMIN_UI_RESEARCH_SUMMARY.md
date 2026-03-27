# Admin UI Stack Research Summary

**Project:** IronMonkey v1.2 Admin UI (Blazor Server + Tailwind CSS)
**Researched:** 2026-03-27
**Confidence:** HIGH

---

## Executive Summary

The Blazor Server admin UI for v1.2 requires **ZERO new NuGet packages** and **ONE external tool** (Tailwind CLI binary):

### Stack Additions
1. **Extend ApiClient** (existing file) with typed methods for admin endpoints
2. **Create JwtAuthenticationStateProvider** (new file) to parse JWT and provide claims to components
3. **Add Tailwind CSS v4 standalone** binary (~15MB, platform-specific executable)
4. **Configure MSBuild** target to auto-generate CSS during builds

### Why This Works
- IronMonkey.Web is already Blazor Server with .NET 10.0 and Aspire service defaults
- HttpClient, AuthenticationStateProvider, [Authorize] attributes are built-in
- Tailwind v4 **auto-detects .razor files without configuration**
- No Node.js or npm dependency needed

### Complexity Assessment
- **Configuration only** — no new architecture, no breaking changes
- **Integration points:** ApiClient → Blazor components, Auth → Components, CSS → Build pipeline
- **Risk:** LOW — all patterns are standard Blazor Server + Tailwind approaches

---

## Key Technical Decisions

### 1. Authentication via Custom AuthenticationStateProvider
**Decision:** Implement custom `JwtAuthenticationStateProvider` to fetch user claims from `/api/auth/me` endpoint

**Rationale:**
- IronMonkey API already issues JWT with `tenant_id`, `user_id`, and `roles` claims
- Blazor Server auth happens server-side (not bypassable like WASM)
- Cache principal for circuit lifetime (no repeated API calls per component initialization)
- Integrates seamlessly with `[Authorize]` attributes and `AuthorizeView` components

**Alternative rejected:** ASP.NET Core Identity scaffolding — IronMonkey uses custom JWT auth model, not Identity passwords

### 2. API Client via IHttpClientFactory
**Decision:** Extend existing `ApiClient` with typed methods using `IHttpClientFactory`

**Rationale:**
- IHttpClientFactory is built-in to ASP.NET Core 10.0; pools connections
- Prevents socket exhaustion as admin UI scales
- ServiceDiscovery (already in ServiceDefaults) resolves `https+http://apiservice` at runtime
- Typed methods enable IntelliSense and prevent typos

**Alternative rejected:** Direct HttpClient.GetFromJsonAsync in components — less maintainable as endpoints grow

### 3. Tailwind CSS v4 Standalone (No npm)
**Decision:** Use Tailwind v4 standalone CLI binary; configure MSBuild target for automated builds

**Rationale:**
- Eliminates Node.js from .NET build pipeline (per PROJECT.md constraint)
- v4 auto-detects .razor files without configuration file
- Single binary download; no package manager complexity
- Integrates with `dotnet build` and `dotnet publish` via MSBuild target

**Alternative rejected:** Tailwind with npm + PostCSS — adds Node.js dependency, complicates CI/CD

### 4. No Component Libraries for MVP
**Decision:** Build MVP with native Razor components + Tailwind CSS only

**Rationale:**
- Admin CRUD pages don't require advanced DataGrids or modal libraries
- Native components are lighter, faster, no external dependencies
- If future phases need complex tables (pagination, sorting), add MudBlazor then
- Keeps initial TTM fast

**Alternative rejected:** Syncfusion, DevExpress — expensive, overkill for MVP, slower startup

---

## Roadmap Implications

### Phase Timeline
Based on this research, the v1.2 Admin UI phases should be:

**Phase 1: UI Foundation & Auth (2-3 weeks)**
- Set up Tailwind CSS build pipeline
- Implement JwtAuthenticationStateProvider
- Create login page + auth redirect flow
- Test [Authorize] guards on protected routes

**Phase 2: Recipe Management (2 weeks)**
- List recipes (GET /api/admin/recipes)
- View recipe details (GET /api/admin/recipes/{id})
- Create recipe UI (POST /api/admin/recipes)
- Preview recipe before applying

**Phase 3: Tenant & User Management (2 weeks)**
- List tenants + approval workflow
- User management within tenant
- Role assignment UI

**Phase 4: System Configuration (2 weeks)**
- Pipeline stage editor
- Custom field definition UI
- Lead routing configuration

**Rationale:** Auth foundation first (blocks all other pages), then admin workflows in order of complexity.

### No Integration Risks
- **ServiceDiscovery:** Already working in IronMonkey.Web (no changes needed)
- **JWT validation:** API service already issues JWT; Blazor just reads claims
- **Database:** No schema changes; all admin operations via existing API endpoints
- **Deployment:** Tailwind binary included in .csproj; outputs generated during build

### Skill Requirements
- **Blazor Server:** Standard patterns (components, parameters, event callbacks)
- **Tailwind CSS:** Utility classes (no custom CSS writing)
- **HTTP client:** Basic async/await with ApiClient methods
- **No:** npm, webpack, PostCSS, advanced JavaScript

---

## Stack Dependencies (Diagram)

```
IronMonkey.Web (Blazor Server)
├── IronMonkey.ServiceDefaults
│   ├── Microsoft.Extensions.ServiceDiscovery (10.1.0)
│   └── Microsoft.Extensions.Http.Resilience (10.1.0)
├── System.Net.Http (IHttpClientFactory)
├── Microsoft.AspNetCore.Components.Authorization
├── System.Security.Claims (ClaimsPrincipal)
└── Tailwind CSS standalone binary (4.x)
    └── Generates: wwwroot/css/output.css

↓ Communicates with

IronMonkey.ApiService (Minimal API)
├── JWT validation (Microsoft.AspNetCore.Authentication.JwtBearer)
├── EF Core (10.0.5)
└── PostgreSQL

(No circular dependencies, clean separation)
```

---

## Critical Integration Points

### 1. JWT Claims Format
**Assumption:** API's `/api/auth/me` returns JSON like:
```json
{
  "userId": "12345",
  "email": "admin@example.com",
  "tenantId": "abc-def-ghi",
  "roles": ["SuperAdmin", "Admin"]
}
```

**Action:** Confirm with API service that this format is what gets returned. If different, update `JwtAuthenticationStateProvider.ParseClaimsFromResponse()`.

### 2. Service Discovery URI Format
**Assumption:** Aspire injects `Services__apiservice__https` env var at runtime, and `https+http://apiservice` resolves to it.

**Verification:** Already working in existing IronMonkey.Web setup. Test with:
```csharp
var httpClient = httpClientFactory.CreateClient();
httpClient.BaseAddress = new Uri("https+http://apiservice");
var response = await httpClient.GetAsync("/health");
```

### 3. Tailwind Scanning
**Assumption:** Tailwind v4 auto-detects and includes classes from:
- `Components/**/*.razor`
- `Components/**/*.razor.cs` (code-behind files)
- `Pages/**/*.razor`

**Verification:** Build and check `output.css` contains expected classes. Tailwind's auto-detection has been verified in 2026 blog posts.

---

## Configuration Validation Checklist

Before starting Phase 1 implementation:

- [ ] Verify `/api/auth/me` endpoint exists and returns correct claim format
- [ ] Test ApiClient can reach backend via ServiceDiscovery
- [ ] Download Tailwind standalone binary (tailwindcss --version works)
- [ ] Create wwwroot/css/input.css and verify output.css generates
- [ ] Test [Authorize] attribute blocks unauthenticated access
- [ ] Verify localhost dev URLs (e.g., https://localhost:7000) work
- [ ] Confirm Blazor hot reload works with CSS changes

---

## Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| API returns unexpected claim format | Check API endpoint before auth implementation; unit test claim parsing |
| Tailwind binary missing during CI/CD build | Include binary in .gitignore; document download step in setup guide or auto-download in MSBuild |
| JWT token expiration during long admin sessions | Circuit lifetime handles this; expired token = next /api/auth/me call fetches fresh state |
| Unauthorized user can access admin pages | [Authorize] attribute enforced server-side; AuthorizeView is supplementary UI decoration |
| CSS output.css not generated in CI/CD | MSBuild target ensures generation during build; test with `dotnet publish` |
| Component library temptation | Explicitly defer to later phases; MVP uses only native Razor + Tailwind |

---

## What's NOT Needed (Scope Exclusions)

- [ ] npm, Node.js, package.json
- [ ] PostCSS, webpack, bundlers
- [ ] Component libraries (MudBlazor, Syncfusion, etc.)
- [ ] ASP.NET Core Identity scaffolding
- [ ] Custom authentication scheme (use existing JWT)
- [ ] Client-side state management (no Redux, Flux, etc.)
- [ ] SPA-style navigation (Blazor Server uses standard Razor routing)
- [ ] GraphQL API (REST endpoints via Minimal API sufficient)

---

## Sources Confirming Stack Choice

| Topic | Source | Confidence |
|-------|--------|-----------|
| Blazor Server HttpClient patterns | Microsoft Learn (.NET 10.0) | HIGH |
| AuthenticationStateProvider implementation | Microsoft Learn (.NET 10.0) + GitHub examples | HIGH |
| Tailwind v4 standalone auto-detection | DEV Community (2026) + Steven Giesel blog | HIGH |
| Tailwind MSBuild integration | GitHub repos (Physer/blazor-tailwind) | HIGH |
| Aspire service discovery | Microsoft Learn + Medium article | HIGH |
| No new packages needed | .NET 10.0 built-in features | HIGH |

---

**Conclusion:** The admin UI stack is **straightforward, low-risk, and uses only standard Blazor Server patterns**. All needed capabilities are built-in to .NET 10.0. The only external addition is the Tailwind standalone binary, which has no dependencies and can be easily versioned in the codebase or CI/CD pipeline.
