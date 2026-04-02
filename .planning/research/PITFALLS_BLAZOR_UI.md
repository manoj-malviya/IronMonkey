# Domain Pitfalls: Adding Blazor Server Admin UI with Tailwind CSS to .NET Aspire Multi-Tenant CRM

**Domain:** Blazor Server + Tailwind CSS integration with existing .NET Aspire multi-tenant backend
**Researched:** 2026-03-27
**Milestone:** IronMonkey v1.2 — Admin UI foundation
**Confidence:** MEDIUM-HIGH (verified against current 2026 GitHub issues, official docs, and community patterns)

---

## Critical Pitfalls

These mistakes cause data leaks, authentication failures, cross-tenant contamination, or require architectural rewrites.

### Pitfall 1: Silent User Identity Changes on Circuit Reconnect

**What goes wrong:**
When a Blazor Server circuit loses connection and reconnects (network hiccup, browser tab backgrounding), the SignalR circuit rebinds using the current authentication cookie. However, the framework does not enforce that the user identity at reconnect matches the identity at circuit creation. If Cookie A (User1) existed during login, but User2's authentication cookie is now active when the circuit reconnects, the circuit can silently execute under User2's identity.

**Why it happens:**
- Blazor Server circuits are stateful, expecting user identity continuity
- SignalR reconnection uses the current request's authentication principal
- Multiple browser tabs share authentication cookies
- No built-in hook checks user identity consistency across reconnect boundaries
- Admin UI for multi-tenant system means cross-tenant data leaks are possible (User from Tenant A suddenly executing as User from Tenant B)

**Consequences:**
- User A's circuit executes operations as User B (cross-user contamination)
- Admin editing Tenant X's recipes while circuit reconnects to User3 → edits wrong tenant's recipes
- Audit trail misattributes changes (says Admin1 made change but Admin2's circuit executed it)
- Silent failure — no error, just wrong tenant's data modified
- Regulatory compliance violation (data crossed tenant boundaries without detection)

**Prevention:**
- Implement ICircuitHandler to validate user identity on circuit reconnect
- Store user ID + tenant ID in circuit state during initialization (in circuit.User.Identity.Name or custom claims)
- On reconnect, reject or reset circuit if user.TenantId differs from initial circuit's tenant
- Use shorter JWT expiration (15-30 min) with refresh tokens (7 days) so long-lived sessions don't accumulate
- Log all circuit creation/reconnection with user + tenant for audit trail
- Test: verify circuit fails to reconnect if authenticated user changes between connections

**Detection:**
- Circuit handler logs warn-level entries when identity mismatch detected
- Audit log shows operations attributed to different user than expected
- Integration tests with artificial identity switching detect the issue
- Monitoring: alert if circuit logs more than 5 reconnects per user per minute (network problem or attack)

**Phase placement:** Phase 1 (Blazor Auth Guards) — Must implement circuit identity validation before any authenticated endpoints

---

### Pitfall 2: DbContext Pooling Breaks Multi-Tenant Data Isolation

**What goes wrong:**
`AddDbContextPool` in dependency injection reuses DbContext instances across requests. A scoped TenantDbContext requires `ITenantContext` injected to know which tenant's database to query. But a pooled context holds stale tenant context from a previous request's tenant, causing Tenant A's circuit to accidentally query Tenant B's database.

**Why it happens:**
- EF Core's context pooling optimizes by caching and reusing context instances across DI scopes
- DbContext lifetime pools across multiple DI scopes — context is reset between uses but connection string isn't re-evaluated
- ITenantContext (tenant resolver from JWT claims) is scoped and not properly re-evaluated when pooled context is reused
- Global query filters on TenantDbContext rely on current tenant being set correctly; pooled contexts may have stale filters
- Easy mistake: copy AddDbContextPool pattern from single-tenant examples without understanding multi-tenant implications
- IronMonkey already uses AddDbContext but subsequent developers might optimize to pooling

**Consequences:**
- Admin viewing Tenant A's recipes sees Tenant B's recipes instead (data read from wrong database)
- Admin creates recipe in Tenant B, but it's saved to Tenant A's database (data write crosses tenant boundary)
- Regulatory compliance violation (HIPAA, GDPR — unauthorized data exposure between customers)
- Silent data corruption; no error message, just wrong tenant's data visible
- Very difficult to detect in testing (works in single-tenant tests, fails only in multi-tenant scenarios)

**Prevention:**
- Use `AddDbContext` (not AddDbContextPool) for TenantDbContext — accept slight performance cost for correctness
- If pooling is absolutely required for performance, implement custom context factory that validates tenant before reuse
- Add unit test: verify each request gets a fresh TenantDbContext instance with correct tenant connection string
- Global query filters on TenantDbContext enforce TenantId filtering (IronMonkey already implements this — keep it)
- In integration tests: attempt to access Tenant B's data from Tenant A's context, verify it fails or returns empty
- Document in CLAUDE.md: "Never use AddDbContextPool with TenantDbContext; always use AddDbContext"

**Detection:**
- Multi-tenant integration tests fail (wrong tenant's data visible in results)
- Audit log shows operations crossing tenant boundaries with same DbContext pool ID
- EF Core query logs show filtered queries with wrong TenantId parameter value
- Performance profiling shows unusual cache hits from different tenants

**Phase placement:** Phase 1 (DI Setup) — Validate and lock DbContext registration strategy before API-to-UI wiring

---

### Pitfall 3: JWT Token Expiration + Circuit Reconnect = Silent API Failures

**What goes wrong:**
Blazor Server stores JWT token in ProtectedSessionStorage. When circuit reconnects after network hiccup (user steps away, tab backgrounded), if the JWT has expired in the interim, the HttpClient uses the stale token. API rejects with 401 Unauthorized, but the circuit doesn't automatically refresh the token. The user sees a generic "Connection lost" error instead of "Please re-authenticate."

**Why it happens:**
- JWT tokens have expiration (typically 15-60 min for admin UI)
- Circuit reconnect does not trigger token refresh automatically
- ProtectedSessionStorage is per-browser tab; tokens don't sync across tabs
- HttpClient doesn't have automatic token refresh middleware by default in Blazor Server
- Admin might step away during lunch (token expires), returns to find circuit disconnected
- DelegatingHandler must be explicitly configured; developers often skip this

**Consequences:**
- Admin tries to submit form, gets "Connection lost" instead of helpful auth error
- Admin cannot complete tasks; must manually refresh/re-login, losing form state and context
- Poor user experience; admin thinks system is broken instead of "please re-auth"
- Silent fallure to API calls; no clear error that token is expired

**Prevention:**
- Implement custom DelegatingHandler that intercepts 401 Unauthorized responses
- On 401, attempt to refresh JWT via dedicated `/api/auth/refresh` endpoint
- Store refresh token separately from access token (in httpOnly cookie for security)
- Validate token expiration on circuit initialization + periodically in AuthenticationStateProvider
- Configure HttpClient with token refresh handler: `services.AddHttpClient("AdminClient").AddHttpMessageHandler<TokenRefreshHandler>()`
- On failed refresh (401), redirect to login page instead of retrying
- Set reasonable JWT expiration (15-30 min for admin, longer for refresh token 7 days)
- Test circuit reconnect with artificially expired JWT; verify refresh attempt occurs and succeeds

**Detection:**
- Monitoring: track refresh token endpoint calls; spike = sign of many expired tokens
- Integration test: set JWT expiration to 1 sec, wait, call API, verify automatic refresh occurs
- Browser console: look for multiple 401 errors instead of single refresh + retry

**Phase placement:** Phase 1 (Auth Guards) — Build token refresh into HttpClient DelegatingHandler configuration

---

### Pitfall 4: HttpClient Missing JWT Token in Authorization Header

**What goes wrong:**
Blazor component successfully authenticates and stores JWT token in ProtectedSessionStorage. Component then calls API via HttpClient: `var recipes = await http.GetAsync("/api/recipes")`. But the Authorization header is not included. API rejects with 401 Unauthorized. Admin can log in but can't access any admin functionality.

**Why it happens:**
- Token stored in ProtectedSessionStorage requires explicit extraction + header injection
- HttpClient doesn't automatically include stored tokens (unlike WASM with localStorage)
- Developer forgets to implement DelegatingHandler that reads token and adds Authorization header
- Blazor Server's server-side nature makes token access non-trivial (must use ProtectedSessionStorage, not localStorage)
- Copy-paste of HttpClient setup from examples that assume token is always in header/cookie

**Consequences:**
- All API calls fail with 401 Unauthorized
- Admin UI appears broken: "Access denied" on every page
- Admin can log in (login endpoint is often AllowAnonymous for testing) but can't load data
- Confusing error state; no clear indication that token wasn't sent

**Prevention:**
- Create custom DelegatingHandler (or IHttpClientFactory config) that:
  1. Reads token from ProtectedSessionStorage in each request
  2. Attaches token to Authorization header: `request.Headers.Authorization = new("Bearer", token)`
- Register handler with HttpClient:
  ```csharp
  services.AddHttpClient("AdminClient")
    .ConfigureHttpClient(client => client.BaseAddress = new Uri("https+http://apiservice"))
    .AddHttpMessageHandler<BearerTokenHandler>();
  ```
- Inject "AdminClient" (IHttpClientFactory or typed HttpClient) into components that call API
- Test: verify Authorization header is present in HTTP requests to API
- In integration tests: log all outgoing requests, assert Authorization header value matches expected token

**Detection:**
- API logs show 401 responses with no Authorization header from Blazor client
- Developer: use browser DevTools Network tab, inspect request headers
- Integration test assertion: `request.Headers.Authorization.Scheme == "Bearer" && request.Headers.Authorization.Parameter == expectedToken`

**Phase placement:** Phase 1 (API Client Setup) — Configure HttpClient with BearerToken handler before any API integration tests

---

## Moderate Pitfalls

### Pitfall 5: Tailwind CSS Hot Reload Doesn't Work with Dotnet Watch

**What goes wrong:**
When developing with `dotnet watch`, changing Tailwind CSS classes in a .razor component doesn't update styles in the browser. The page reloads (dotnet watch hot injection), but old CSS remains. Developer changes `class="text-blue-500"` to `class="text-red-500"`, but browser still shows blue.

**Why it happens:**
- Dotnet watch optimization: it injects code changes directly into running .NET host, skipping MSBuild
- Tailwind CSS standalone CLI build step lives in MSBuild (PostBuild or custom target)
- MSBuild isn't invoked by dotnet watch, so Tailwind CLI never reruns
- Tailwind CSS recompiles only when MSBuild triggers (full rebuild)
- Developers expect hot reload to include styling (common in modern Node.js + Tailwind workflows)

**Consequences:**
- Confusing development experience; CSS changes don't appear immediately
- Wastes time debugging: changes a Tailwind class, page reloads, sees no change, questions if class is correct
- Developers think Tailwind configuration is broken when it's just the watch mode
- Forces developer to manually stop/start `dotnet watch` or run `tailwindcss --watch` in separate terminal
- Defeats purpose of hot reload for styling

**Prevention:**
- Document in CLAUDE.md: "During development, run TWO terminals: `dotnet watch` in one, `tailwindcss --watch` in another"
- Create a Makefile or script that runs both processes in parallel
- Or configure MSBuild to always run Tailwind (trade off: rebuild slower, but styling always consistent)
- Add custom target to .csproj that runs Tailwind as pre-build step: `<Target Name="TailwindBuild" BeforeTargets="Build"><Exec Command="tailwindcss ..." /></Target>`
- Alternatively, use `dotnet watch --no-hot-reload` to force full rebuild (includes Tailwind)
- For 2025+: investigate TailwindCSS.Blazor library which may provide integrated watch support
- Test: change Tailwind class, verify CSS file contains updated class, verify browser loads new CSS (use Network tab in DevTools)

**Detection:**
- Developer notices CSS not updating on reload
- Visual regression in QA: "This component doesn't match what I see in dev"
- CI/CD catches styling issues that weren't visible during local development

**Phase placement:** Phase 1 (UI Foundation) — Document and configure Tailwind watch strategy upfront; include in CLAUDE.md

---

### Pitfall 6: Multiple Blazor Circuits for Same User = Stale State + Memory Leaks

**What goes wrong:**
Admin opens admin UI in two browser tabs (or browser auto-restores tabs on startup). Each tab creates a separate Blazor Server circuit on the server. Both circuits have independent state and connection to the same TenantDbContext. If Tab1 updates a recipe configuration, Tab2 doesn't see the change (stale state shows old recipe). Each circuit holds memory (DbContext instance, service instances, component state), multiplying memory usage.

**Why it happens:**
- Blazor Server creates a circuit per physical connection (not per user)
- Each circuit is independent; no built-in state sync between circuits for same user
- Browser tabs are isolated; Tab1's circuit doesn't communicate with Tab2's circuit
- No cache invalidation or state sharing across circuits
- Admin UI is heavily stateful (form edits, pagination state, filter selections)
- Long-lived admin sessions (8-hour work day) with multiple circuits = significant memory accumulation

**Consequences:**
- Admin edits recipe in Tab1, switches to Tab2, sees old recipe version (confused about which version is current)
- Admin creates new tenant in Tab1, Tab2 still shows old tenant list (thinks provisioning failed)
- Server memory per admin user = (Circuit memory) × (number of tabs) — scales poorly
- High-load testing with realistic multi-tab scenarios reveals unexpected memory usage
- Difficult to debug: "Why didn't my change appear?" — admin unaware of separate circuits
- Memory not released when tab closed if circuit cleanup is delayed

**Prevention:**
- Implement ICircuitHandler that limits circuits per user (reject/warn if user already has circuit)
  ```csharp
  public class SingleCircuitPerUserHandler : CircuitHandler {
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken) {
      var userId = circuit.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
      if (_activeCurcuits.ContainsKey(userId) && _activeCircuits[userId].Count > 1) {
        // Log warning or reject
      }
      return Task.CompletedTask;
    }
  }
  ```
- Or detect multi-circuit scenario and broadcast state changes via SignalR Hub to invalidate caches
- Document in UI: warning if same user logged in from multiple places
- Implement cache invalidation: when recipe updated in DB, publish event to all circuits for that user, trigger refresh
- Prefer SPA-style navigation (single circuit, client-side routing) over multiple tabs
- Monitor and alert: CircuitCount / UserCount ratio > threshold = possible multi-tab scenario
- In integration tests: verify no more than 1 circuit per user

**Detection:**
- Integration tests fail if 2nd circuit created for same user
- Monitoring dashboard: track CircuitsActive / UniqueUsers ratio
- Memory profiling: identify multiple DbContext instances with same UserId in circuit state
- Admin feedback: "Changes from Tab1 don't appear in Tab2"

**Phase placement:** Phase 1 (Blazor Auth Guards) — Implement circuit lifecycle management and per-user circuit limits

---

### Pitfall 7: Tenant Context Not Validated in API Endpoints

**What goes wrong:**
Admin from Tenant A logs in, receives JWT with `tenant_id: 1` claim. Component calls API: `GET /api/recipes`. API endpoint handler retrieves recipe list, but doesn't validate tenant_id from claims. Instead, it returns all recipes in database or defaults to Tenant 0. Admin sees Tenant B's recipes.

**Why it happens:**
- JWT token includes tenant_id claim; developer assumes presence of claim means it's validated
- Multi-tenant endpoint requires explicit: `var tenantId = User.FindFirst("tenant_id")?.Value` and filtering
- Copy-paste endpoint code from single-tenant examples (no tenant validation)
- Easy to miss: no compiler error if tenant validation is omitted
- Silent data leakage: API returns data with no error message
- IronMonkey already implements tenant validation via IUserContext, but new endpoints might bypass it

**Consequences:**
- Admin views recipes from Tenant A while logged into Tenant B
- Creates recipe in Tenant B, but it's saved to Tenant A's database
- Data crosses tenant boundaries silently
- Regulatory compliance violation (unauthorized data exposure)
- Very difficult to detect in testing if test data doesn't distinguish tenant A from B clearly

**Prevention:**
- Every multi-tenant API endpoint MUST:
  1. Extract tenant from JWT: `var tenantId = User.FindFirst("tenant_id")?.Value`
  2. Validate tenant is present: if missing, return 401 Unauthorized
  3. Use IUserContext service to abstract tenant extraction (IronMonkey pattern)
  4. Filter all queries by tenant: `context.Recipes.Where(r => r.TenantId == tenantId)`
- Use query filters in DbContext to enforce filtering automatically (IronMonkey already does this)
- Add integration test: call endpoint with token from Tenant A, verify it only returns Tenant A's data
- Add cross-tenant test: call endpoint with Tenant A's token but request Tenant B's resource, verify 403 Forbidden
- Code review checklist: "Does this endpoint validate tenant from JWT?"

**Detection:**
- Integration tests verify tenant isolation per endpoint
- Audit log shows operations crossing tenants with same user
- Cross-tenant load tests reveal data leaks
- API logs show queries without TenantId filter (suspicious)

**Phase placement:** Phase 1 (API Layer) — Ensure ALL new admin endpoints validate tenant context (IronMonkey already has pattern; enforce it)

---

### Pitfall 8: IDbContextFactory Registration Causes "Cannot Resolve Scoped Service" Errors

**What goes wrong:**
When using IDbContextFactory<TenantDbContext> to create fresh DbContext instances in Blazor components, registering factory as singleton but it depends on scoped services (like AuthenticationStateProvider or IUserContext) causes a DI resolution error at startup: "Cannot resolve scoped service 'IUserContext' from root provider."

**Why it happens:**
- IDbContextFactory is typically registered as singleton (one factory instance per application)
- Factory often needs to access current user/tenant to create context for correct database
- IUserContext, AuthenticationStateProvider are scoped (per-circuit in Blazor Server)
- .NET DI container prevents singleton from depending on scoped services (incorrect lifetime hierarchy)
- Developers copy IDbContextFactory pattern without understanding dependency lifetime implications

**Consequences:**
- Application fails at startup during DI container configuration
- Error message doesn't clearly point to the lifetime mismatch (buried in inner exception)
- Developer spends time debugging DI instead of business logic
- Blocks development until DI is fixed

**Prevention:**
- Prefer `AddDbContext` (without pooling) instead of IDbContextFactory for Blazor Server
- If factory is necessary, inject factory into component/page (scoped), not into singleton services
- Or use factory that doesn't depend on scoped services; pass tenant/user info as parameter: `factory.CreateDbContext(tenantId)`
- Or use IServiceProvider.CreateScope() inside factory to create scoped context:
  ```csharp
  using var scope = provider.CreateScope();
  var userContext = scope.ServiceProvider.GetRequiredService<IUserContext>();
  ```
- Test: verify app starts with no DI resolution errors
- Add to startup tests: `services.BuildServiceProvider();` — verify no exceptions

**Detection:**
- Application startup fails immediately with DI error
- Unit test DI registration: `new ServiceCollection().AddServices().BuildServiceProvider()` throws
- Local development catches this immediately on `dotnet build` or `dotnet run`

**Phase placement:** Phase 1 (DI Setup) — Validate context factory strategy in startup tests; prefer AddDbContext

---

## Minor Pitfalls

### Pitfall 9: Blazor Server Performance Degradation with Large Admin Forms

**What goes wrong:**
Admin form for configuring custom fields has 200+ fields (per-tenant custom field definitions). Form renders slowly (1-2 sec lag after each keystroke). Browser memory usage spikes. Admin complains form is "sluggish" and unusable.

**Why it happens:**
- Each form field = EditForm component + validation handler + parameter binding
- EditForm re-renders child components when any field changes
- 200 fields × re-render overhead = expensive DOM updates
- Each keystroke sends WebSocket message to server (validation state, field value)
- Deep component hierarchy (Form > FieldGroup > Field) means each re-render cascades
- Network latency amplifies: keystroke → server → re-render → client → display

**Consequences:**
- Form lags when typing (noticeable 100-500ms delay)
- Browser memory usage spikes to 500MB+ for large form
- Admin can't efficiently edit complex configurations
- Poor user experience; admin abandons task or uses API instead

**Prevention:**
- Use Virtualize<TItem> component for long lists of fields (render only visible items on screen)
- Implement immutable parameters (string, int, bool, DateTime) so Blazor skips re-render if value unchanged
- Debounce validation: batch field changes, validate every 500ms instead of per-keystroke
- Split large forms into tabs or collapsible sections (render sections on-demand)
- Load fields in batches or paginate (100 fields per section)
- Flatten component hierarchy: avoid nested component chains
- Use @key directive to help Blazor identify components correctly
- Test with 200+ field form; profile rendering time with browser DevTools Performance
- Set component parameters as `[Parameter] public string? Value { get; set; }` — allows Blazor to track changes

**Detection:**
- Performance profiling: render time > 100ms per keystroke
- Browser DevTools Network: WebSocket messages > 50/sec for form interaction
- Memory profiling: single form instance > 100MB memory
- QA feedback: "Form is sluggish with many fields"

**Phase placement:** Phase 2-3 (Component Architecture) — Build form patterns that support large field counts; start with Virtualize, tabs, pagination

---

### Pitfall 10: Tailwind CSS Content Paths Miss .razor File Extensions

**What goes wrong:**
tailwind.config.js content array includes glob pattern for components but Tailwind doesn't find or finds stale .razor files. CSS output is missing Tailwind classes used in components. Styles appear as "undefined" in browser DevTools (class in HTML, not in stylesheet).

**Why it happens:**
- tailwind.config.js content paths use glob patterns to find files to scan for classes
- Razor files have .razor extension, not .html; pattern might exclude them
- Path might be incorrect: `./Components/**/*.razor` from wrong working directory
- MSBuild publishes CSS before Tailwind runs (build order issue)
- Tailwind runs before content files are generated/copied
- Case sensitivity on Linux (tailwind.config.js might use Windows paths)

**Consequences:**
- Tailwind classes are purged in production build (not recognized as "used")
- Styles don't apply: `class="w-1/2 text-blue-500"` renders but no color or width
- Admin UI looks broken or severely unstyled
- Very difficult to debug: CSS is in output file, but browser DevTools shows class not in stylesheet
- Styling works in dev (watch mode), breaks in production (full purge)

**Prevention:**
- In tailwind.config.js, ensure content includes all .razor paths:
  ```javascript
  content: [
    "./Components/**/*.razor",
    "./Pages/**/*.razor",
    "./Shared/**/*.razor",
    "./Layouts/**/*.razor"
  ]
  ```
- Run Tailwind with `--watch` during development to see if it detects content file changes
- Build order: Tailwind runs as PRE-BUILD step, generates CSS, then `dotnet build` includes CSS file
- Add MSBuild target:
  ```xml
  <Target Name="TailwindBuild" BeforeTargets="Build">
    <Exec Command="tailwindcss -i ./Styles/input.css -o ./wwwroot/css/app.css" />
  </Target>
  ```
- Test: `grep "w-1/2" wwwroot/css/app.css` should find the class after build
- Verify content scanned: `tailwindcss --content "./Components/**/*.razor" --output app.css` (should show "content scanned: X files")

**Detection:**
- CSS output file missing expected classes: `grep "text-blue-500" app.css` returns nothing
- Browser DevTools: class is in HTML but not in stylesheet (Styles panel shows class as "undefined" or "from stylesheet...")
- Tailwind CLI output shows "content scanned: 0 files" (paths are wrong)
- Visual regression: styled component in dev, unstyled in production

**Phase placement:** Phase 1 (UI Foundation) — Configure tailwind.config.js correctly and verify before any component styling

---

### Pitfall 11: Blazor Server Circuit State Not Preserved on Reconnect

**What goes wrong:**
Admin fills out a large form, network briefly drops (1 sec), circuit reconnects. Form state is lost—all entered values disappear. Admin must re-enter everything.

**Why it happens:**
- Blazor Server circuits store component state in server memory
- On network disconnection > reconnection timeout, circuit is discarded
- Reconnected request creates new circuit with blank state
- Form values held only in component state, not persisted
- No automatic state restoration mechanism in older Blazor versions

**Consequences:**
- Admin loses work when network hiccup occurs
- Poor user experience; admin frustrated by data loss
- Workaround: admin saves form frequently (manual intervention)
- May deter use of admin UI for complex configurations

**Prevention:**
- Implement [SupplyParameterFromPersistentComponentState] in Blazor Web (.NET 9+) to persist component state
- Or manually implement form state persistence: save form to browser localStorage on change
- On component init, restore from localStorage if available
- Implement circuit handler that logs disconnect/reconnect duration; alert admin if > threshold
- For critical forms, implement "auto-save" to API every few seconds
- Test: disconnect circuit artificially, verify state is restored on reconnect
- Document: "Your form state is saved locally; if disconnected, it will be restored when you reconnect"

**Detection:**
- Integration test: fill form, simulate circuit disconnect, reconnect, verify form state restored
- Admin feedback: "My form was cleared after reconnect"
- Monitoring: track circuit disconnections > 30 sec (likely to lose state)

**Phase placement:** Phase 2-3 (Form Architecture) — Implement form state persistence for complex multi-field forms

---

## Phase-Specific Warnings

| Phase | Topic | Likely Pitfall | Mitigation |
|-------|-------|----------------|-----------|
| Phase 1: UI Foundation | DI Registration | DbContext pooling breaks tenant isolation | Use AddDbContext, not AddDbContextPool; add integration test |
| Phase 1: Auth Guards | Circuit Lifecycle | Silent user identity change on reconnect | Implement ICircuitHandler with identity validation |
| Phase 1: Auth Guards | Token Refresh | Expired JWT after circuit reconnect | Custom DelegatingHandler for auto-refresh with 401 interception |
| Phase 1: Auth Guards | HttpClient Config | Token not passed in Authorization header | Custom DelegatingHandler + ProtectedSessionStorage extraction |
| Phase 1: API Client Setup | Token Management | JWT stored but not validated | Implement BearerTokenHandler; test Authorization header presence |
| Phase 1: Tailwind Integration | Standalone CLI | CSS hot reload broken by dotnet watch | Document two-terminal setup: dotnet watch + tailwind --watch |
| Phase 1: Tailwind Config | Content Paths | Tailwind misses .razor file classes | Verify tailwind.config.js content glob includes all .razor paths |
| Phase 1: Error Handling | 401 Responses | API returns 401 but no refresh attempt | DelegatingHandler must intercept 401, refresh token, retry |
| Phase 2: Recipe Management | Component Architecture | Form rendering slow with 200+ fields | Use Virtualize, batch loading, tabs, debounce validation |
| Phase 2: State Management | Multi-Circuit | Multiple circuits for same user = stale state | Implement ICircuitHandler to limit circuits per user |
| Phase 2: Form Persistence | Circuit Disconnect | Form state lost on network hiccup | Persist form state to localStorage; restore on reconnect |
| Multi-tenant Integration | API Tenant Context | No tenant validation in endpoints | Every endpoint: extract tenant from JWT, validate, filter by tenant |
| Multi-tenant Integration | DbContext Factory | Factory depends on scoped service → DI error | Use AddDbContext instead; never use AddDbContextPool |

---

## Prevention Checklist for Phase 1 Implementation

Before shipping Phase 1 (Blazor Auth Guards), verify:

- [ ] **Circuit Identity Validation:** ICircuitHandler created; validates user identity on reconnect, logs mismatches
- [ ] **DbContext Registration:** TenantDbContext uses `AddDbContext()` (not pooling); verified in startup tests
- [ ] **Token Refresh:** DelegatingHandler implemented; intercepts 401, refreshes token, retries request; tested with expired JWT
- [ ] **Token Storage:** JWT in ProtectedSessionStorage; refresh token in httpOnly cookie (or session storage if no refresh)
- [ ] **HttpClient:** Custom BearerTokenHandler automatically attaches Authorization header from stored token; tested
- [ ] **Tenant Validation:** Every admin API endpoint extracts tenant_id from JWT claims; integration tests verify isolation
- [ ] **Tailwind Config:** tailwind.config.js verified to include all .razor paths; CSS output contains expected classes
- [ ] **Build Pipeline:** Tailwind runs as pre-build MSBuild target; CSS generated before dotnet build completes
- [ ] **Dev Environment:** CLAUDE.md documents two-terminal setup and includes example commands
- [ ] **Integration Tests:** Cover circuit reconnect, token refresh, token expiration, tenant isolation per endpoint, DbContext lifecycle
- [ ] **Monitoring:** Logs track circuit creation/reconnect/identity changes, token refresh attempts, 401 errors, cross-tenant queries
- [ ] **Documentation:** Circuit handlers, token management, tenant validation patterns documented in CLAUDE.md

---

## Sources

- [ASP.NET Core Blazor authentication and authorization | Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/?view=aspnetcore-10.0)
- [Blazor Server: Detect and prevent silent user identity changes on SignalR circuit reconnect · Issue #65272 · dotnet/aspnetcore](https://github.com/dotnet/aspnetcore/issues/65272)
- [ASP.NET Core Blazor SignalR guidance | Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/signalr?view=aspnetcore-9.0)
- [We Built Multi-Tenancy Into a Blazor App. Here's Every Layer Preventing Data Leaks. - DEV Community](https://dev.to/octobernorth/we-built-multi-tenancy-into-a-blazor-app-heres-every-layer-preventing-data-leaks-3fbc)
- [Multi-tenancy with EF Core in Blazor Server Apps | Developer for Life](https://blog.jeremylikness.com/blog/multitenancy-with-ef-core-in-blazor-server-apps/)
- [Blazor Server - EF Core Quirks | Ben Sampica](https://www.bensampica.com/blog/blazordbcontext/)
- [Tailwind CSS v4 Standalone in Blazor WebAssembly | DEV Community](https://dev.to/cristiansifuentes/tailwind-css-v4-standalone-in-blazor-webassembly-a-clean-native-integration-for-the-net-26lk)
- [Blazor and Tailwind - Quick Setup Without npm | Blazorise](https://blazorise.com/blog/blazor-and-tailwind-quick-setup-without-npm/)
- [Integrating Tailwind CSS in Blazor with Hot Reload | Medium](https://medium.com/@shenets.andrei/integrating-tailwind-css-in-blazor-with-hot-reload-a8a1d043dc81)
- [Tailwind CSS in .NET 9 Blazor: Fixing Hot Reload Issues | Medium](https://medium.com/@pinyo.rungoral/tailwind-css-in-net-9-blazor-fixing-hot-reload-issues-5ccc49a37954)
- [.NET Aspire Standalone — Blazor Server with Web API | Medium](https://medium.com/@pieter.artorius.vanzyl/net-aspire-standalone-blazor-server-with-web-api-77abd45803a0)
- [JWT Authentication in Blazor 8: Production Implementation Guide | LJBLab](https://www.ljblab.dev/blazor-jwt-authentication-deep-dive)
- [ASP.NET Core Blazor rendering performance best practices | Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/rendering?view=aspnetcore-10.0)
- [Blazor Server App in .NET 10 enters bugged state on circuit resume | GitHub #64607](https://github.com/dotnet/aspnetcore/issues/64607)
- [ASP.NET Core Blazor server-side state management | Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/state-management/server?view=aspnetcore-10.0)
