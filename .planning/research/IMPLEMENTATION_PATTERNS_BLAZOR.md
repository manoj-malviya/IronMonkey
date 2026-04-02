# Blazor Server Implementation Patterns for IronMonkey v1.2

**Purpose:** Actionable code patterns and configuration for Phase 1 implementation
**Based on:** Pitfall research + IronMonkey architecture + 2026 best practices
**Audience:** Developers implementing Blazor auth guards and API client integration

---

## 1. Circuit Identity Validation (ICircuitHandler)

**Problem:** Circuit reconnect can silently execute under wrong user's identity.

**Solution:** Validate user identity on circuit reconnect.

```csharp
// IronMonkey.Web/CircuitHandlers/IdentityValidationCircuitHandler.cs
namespace IronMonkey.Web.CircuitHandlers;

public class IdentityValidationCircuitHandler : CircuitHandler
{
    private readonly ILogger<IdentityValidationCircuitHandler> _logger;
    private readonly IUserContext _userContext;

    public IdentityValidationCircuitHandler(
        ILogger<IdentityValidationCircuitHandler> logger,
        IUserContext userContext)
    {
        _logger = logger;
        _userContext = userContext;
    }

    private static readonly ConcurrentDictionary<string, (string UserId, Guid TenantId)> CircuitIdentities = new();

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var userId = circuit.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantIdClaim = circuit.User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(tenantIdClaim))
        {
            _logger.LogWarning("Circuit opened without user/tenant identity");
            return Task.CompletedTask;
        }

        if (!Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            _logger.LogWarning("Circuit opened with invalid tenant_id claim");
            return Task.CompletedTask;
        }

        CircuitIdentities[circuit.Id] = (userId, tenantId);
        _logger.LogInformation("Circuit {CircuitId} opened for user {UserId} tenant {TenantId}",
            circuit.Id, userId, tenantId);

        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        CircuitIdentities.TryRemove(circuit.Id, out _);
        _logger.LogInformation("Circuit {CircuitId} closed", circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        // Called when reconnecting after network loss
        var currentUserId = circuit.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var currentTenantId = circuit.User.FindFirst("tenant_id")?.Value;

        if (!CircuitIdentities.TryGetValue(circuit.Id, out var originalIdentity))
        {
            _logger.LogWarning("Circuit {CircuitId} reconnect: no original identity stored", circuit.Id);
            return Task.CompletedTask;
        }

        if (currentUserId != originalIdentity.UserId ||
            (Guid.TryParse(currentTenantId, out var tenantId) && tenantId != originalIdentity.TenantId))
        {
            _logger.LogError(
                "SECURITY: Circuit {CircuitId} identity mismatch on reconnect. " +
                "Original: user={OriginalUserId} tenant={OriginalTenant}. " +
                "Now: user={CurrentUserId} tenant={CurrentTenant}",
                circuit.Id, originalIdentity.UserId, originalIdentity.TenantId,
                currentUserId, currentTenantId);

            // Optionally: throw exception to force circuit reset
            throw new InvalidOperationException(
                $"Circuit {circuit.Id} identity mismatch on reconnect");
        }

        _logger.LogInformation("Circuit {CircuitId} reconnected successfully (identity validated)",
            circuit.Id);

        return Task.CompletedTask;
    }
}

// Register in Program.cs:
// builder.Services.AddScoped<IdentityValidationCircuitHandler>();
```

**Integration in Program.cs:**
```csharp
builder.Services.AddScoped<IdentityValidationCircuitHandler>();
// ... other services ...
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
```

**Testing:**
```csharp
// IronMonkey.Tests/Integration/CircuitHandlerTests.cs
[Fact]
public async Task CircuitReconnect_WithDifferentUser_ThrowsInvalidOperationException()
{
    var circuit1 = new Circuit { Id = "circuit-1", User = GetClaimsPrincipal("user1", "tenant-a") };
    var handler = new IdentityValidationCircuitHandler(_logger, _userContext);

    await handler.OnCircuitOpenedAsync(circuit1, CancellationToken.None);

    // Simulate reconnect with different user
    var circuit2 = new Circuit { Id = "circuit-1", User = GetClaimsPrincipal("user2", "tenant-b") };

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => handler.OnConnectionUpAsync(circuit2, CancellationToken.None));

    Assert.Contains("identity mismatch", ex.Message);
}
```

---

## 2. Token Refresh Handler (DelegatingHandler)

**Problem:** JWT expires during circuit lifetime; API returns 401; no automatic refresh occurs.

**Solution:** Custom DelegatingHandler intercepts 401, refreshes token, retries request.

```csharp
// IronMonkey.Web/HttpHandlers/BearerTokenHandler.cs
namespace IronMonkey.Web.HttpHandlers;

public class BearerTokenHandler : DelegatingHandler
{
    private readonly ProtectedLocalStorage _storage;
    private readonly ILogger<BearerTokenHandler> _logger;

    public BearerTokenHandler(
        ProtectedLocalStorage storage,
        ILogger<BearerTokenHandler> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Get current token from storage
        var tokenResult = await _storage.GetAsync<string>("auth_token");
        if (tokenResult.Success && !string.IsNullOrEmpty(tokenResult.Value))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);
        }

        var response = await base.SendAsync(request, cancellationToken);

        // If 401, attempt token refresh
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogInformation("Received 401, attempting token refresh");

            var refreshToken = await _storage.GetAsync<string>("refresh_token");
            if (refreshToken.Success && !string.IsNullOrEmpty(refreshToken.Value))
            {
                var newToken = await RefreshTokenAsync(refreshToken.Value, cancellationToken);
                if (newToken != null)
                {
                    // Store new token
                    await _storage.SetAsync("auth_token", newToken);

                    // Retry original request with new token
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
                    response = await base.SendAsync(request, cancellationToken);

                    _logger.LogInformation("Token refresh successful, request retried");
                }
                else
                {
                    _logger.LogWarning("Token refresh failed, user must re-authenticate");
                    // Redirect to login handled by AuthenticationStateProvider
                }
            }
        }

        return response;
    }

    private async Task<string?> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https+http://apiservice/api/auth/refresh");
            request.Content = JsonContent.Create(new { refreshToken });

            var response = await base.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsAsync<RefreshTokenResponse>(cancellationToken);
                return content?.AccessToken;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh request failed");
            return null;
        }
    }

    private record RefreshTokenResponse(string AccessToken);
}

// Register in Program.cs:
// builder.Services.AddHttpClient("AdminClient")
//     .ConfigureHttpClient(client => client.BaseAddress = new Uri("https+http://apiservice"))
//     .AddHttpMessageHandler<BearerTokenHandler>();
```

**Testing:**
```csharp
[Fact]
public async Task SendAsync_On401_RefreshesTokenAndRetries()
{
    var handler = new BearerTokenHandler(_storage, _logger);
    var mockInner = new MockHttpMessageHandler();

    // First response: 401, Second response: 200
    mockInner.When("*").Respond(HttpStatusCode.Unauthorized);
    mockInner.When("*").Respond(HttpStatusCode.OK);

    handler.InnerHandler = mockInner;

    var client = new HttpClient(handler);
    var request = new HttpRequestMessage(HttpMethod.Get, "https://api/recipes");

    var response = await client.SendAsync(request);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    // Verify refresh token was called
    Assert.Contains("auth/refresh", mockInner.Requests.Select(r => r.RequestUri.AbsolutePath));
}
```

---

## 3. DbContext Configuration (No Pooling)

**Problem:** AddDbContextPool breaks tenant isolation.

**Solution:** Use AddDbContext with explicit tenant resolution.

```csharp
// IronMonkey.ApiService/Program.cs or IronMonkey.Web/Program.cs
namespace IronMonkey.Web;

extension(WebApplicationBuilder builder)
{
    // DbContext setup for Blazor UI
    builder.Services.AddDbContext<TenantDbContext>((sp, options) =>
    {
        var tenantProvider = sp.GetRequiredService<ITenantContext>();
        var connectionString = tenantProvider.GetConnectionString();

        options.UseNpgsql(connectionString)
            .UseLoggerFactory(LoggerFactory.Create(b => b.AddConsole()));
    });

    // NEVER use AddDbContextPool for multi-tenant systems:
    // builder.Services.AddDbContextPool<TenantDbContext>(...); // WRONG!

    // IUserContext already provides tenant resolution
    builder.Services.AddScoped<ITenantContext, UserContextTenantProvider>();
}
```

**Verification:**
```csharp
// Test startup to ensure DI is configured correctly
[Fact]
public void DbContextIsNotPooled()
{
    var services = new ServiceCollection();
    services.AddServices(); // calls extension methods

    var provider = services.BuildServiceProvider();

    // Should not throw DI error
    var context1 = provider.GetRequiredService<TenantDbContext>();
    var context2 = provider.GetRequiredService<TenantDbContext>();

    // Should be different instances (not pooled)
    Assert.NotSame(context1, context2);
}
```

---

## 4. Tenant Validation in Endpoints

**Problem:** Endpoints don't validate tenant, causing data leaks.

**Solution:** Every endpoint validates tenant from JWT claims.

```csharp
// Example: Recipe management endpoint
// IronMonkey.ApiService/Features/Admin/Recipes/GetRecipesEndpoint.cs
namespace IronMonkey.ApiService.Features.Admin.Recipes;

public class GetRecipesEndpoint : IEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/admin/recipes", Handle)
            .WithName("GetRecipes")
            .WithOpenApi()
            .RequireAuthorization(); // Auth required

        static async Task<Results<Ok<List<RecipeResponse>>, NotFound, ForbiddenHttpResult>>
            Handle(IUserContext userContext, TenantDbContext context, CancellationToken ct)
        {
            // 1. Validate tenant from context (JWT claims)
            if (!userContext.TenantId.HasValue)
            {
                return TypedResults.Forbid();
            }

            var tenantId = userContext.TenantId.Value;

            // 2. Query filtered by tenant (global query filter enforces this, but be explicit)
            var recipes = await context.Recipes
                .Where(r => r.TenantId == tenantId)
                .AsNoTracking()
                .ToListAsync(ct);

            return TypedResults.Ok(recipes.Select(MapToResponse).ToList());
        }
    }

    public record RecipeResponse(Guid Id, string Name, string Description);

    private static RecipeResponse MapToResponse(Recipe recipe) =>
        new(recipe.Id, recipe.Name, recipe.Description);
}

// Integration test: verify tenant isolation
[Fact]
public async Task GetRecipes_ReturnsOnlyCurrentTenantRecipes()
{
    // Create recipes for TenantA and TenantB
    var recipeA = await _context.Recipes.AddAsync(new Recipe { TenantId = TenantA, Name = "Recipe A" });
    var recipeB = await _context.Recipes.AddAsync(new Recipe { TenantId = TenantB, Name = "Recipe B" });
    await _context.SaveChangesAsync();

    // Call endpoint as user from TenantA
    var response = await _client.GetAsync("/api/admin/recipes",
        headers: new Dictionary<string, string> { { "Authorization", $"Bearer {TenantAToken}" } });

    var recipes = await response.Content.ReadAsAsync<List<RecipeResponse>>();

    // Should only return Recipe A
    Assert.Single(recipes);
    Assert.Equal("Recipe A", recipes[0].Name);
}

// Cross-tenant test: verify rejection
[Fact]
public async Task GetRecipes_WithWrongTenant_ReturnsForbidden()
{
    var tenantAToken = GenerateToken(userId: "admin1", tenantId: TenantA);
    var tenantBToken = GenerateToken(userId: "admin2", tenantId: TenantB);

    // Try to access TenantB resource with TenantA token
    var response = await _client.GetAsync("/api/admin/recipes/tenant-b/details",
        headers: new Dictionary<string, string> { { "Authorization", $"Bearer {tenantAToken}" } });

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}
```

---

## 5. Tailwind CSS Configuration

**Problem:** Tailwind doesn't find .razor files; CSS is purged in production.

**Solution:** Configure content paths and MSBuild integration.

```javascript
// IronMonkey.Web/tailwind.config.js
/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./Components/**/*.{razor,html}",
    "./Pages/**/*.{razor,html}",
    "./Shared/**/*.{razor,html}",
    "./Layouts/**/*.{razor,html}",
  ],
  theme: {
    extend: {},
  },
  plugins: [],
}
```

**MSBuild Integration (add to .csproj):**
```xml
<!-- IronMonkey.Web/IronMonkey.Web.csproj -->
<Target Name="TailwindBuild" BeforeTargets="Build">
    <Exec Command="tailwindcss -i ./Styles/input.css -o ./wwwroot/css/app.css" />
</Target>

<Target Name="TailwindMinify" BeforeTargets="Publish">
    <Exec Command="tailwindcss -i ./Styles/input.css -o ./wwwroot/css/app.css --minify" />
</Target>
```

**Development Setup (CLAUDE.md):**
```markdown
## Developing with Tailwind CSS

During development, you must run Tailwind in watch mode alongside dotnet watch:

### Terminal 1: Run Blazor app
```bash
dotnet watch --project IronMonkey.Web
```

### Terminal 2: Watch Tailwind for changes
```bash
cd IronMonkey.Web
tailwindcss -i ./Styles/input.css -o ./wwwroot/css/app.css --watch
```

**Important:** Dotnet watch does not automatically trigger Tailwind recompilation.
Both processes must run simultaneously for styling changes to appear.

### Verification
After making CSS changes, verify the output file was updated:
```bash
grep "your-new-class" IronMonkey.Web/wwwroot/css/app.css
```

If not found, the Tailwind watch process may have missed the change.
```

---

## 6. HttpClient Configuration in Program.cs

**Problem:** Blazor components don't have JWT token injected in requests.

**Solution:** Configure typed HttpClient with BearerTokenHandler.

```csharp
// IronMonkey.Web/Program.cs
namespace IronMonkey.Web;

var builder = WebApplicationBuilder.CreateBuilder(args);

builder.AddServiceDefaults();

// Register Blazor components
builder.Services.AddRazorComponents()
    .AddInteractiveServerRenderMode();

// Add authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = "oidc";
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
.AddOpenIdConnect("oidc", options =>
{
    options.Authority = "https+http://apiservice";
    options.ClientId = "blazor-admin";
    // ... OIDC config
});

// Add services
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();

// Add DbContext
builder.Services.AddDbContext<TenantDbContext>((sp, options) =>
{
    var tenantProvider = sp.GetRequiredService<ITenantContext>();
    options.UseNpgsql(tenantProvider.GetConnectionString());
});

// Add HttpClient with token handler
builder.Services.AddScoped<BearerTokenHandler>();
builder.Services.AddHttpClient("AdminClient")
    .ConfigureHttpClient(client =>
    {
        client.BaseAddress = new Uri("https+http://apiservice");
    })
    .AddHttpMessageHandler<BearerTokenHandler>();

// Register services
builder.Services.AddScoped<ITenantContext, UserContextTenantProvider>();
builder.Services.AddScoped<IdentityValidationCircuitHandler>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
```

---

## 7. Login Endpoint Design

**Problem:** Admin needs to authenticate and receive JWT + refresh token.

**Solution:** Login endpoint returns JWT in claims, refresh token in httpOnly cookie.

```csharp
// IronMonkey.ApiService/Features/Auth/LoginEndpoint.cs
namespace IronMonkey.ApiService.Features.Auth;

public class LoginEndpoint : IEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/auth/login", Handle)
            .WithName("Login")
            .AllowAnonymous();
    }

    public record Request(string Email, string Password);
    public record Response(string AccessToken);

    public static async Task<Results<Ok<Response>, UnauthorizedHttpResult>> Handle(
        Request request,
        IUserContext userContext,
        CentralDbContext centralContext,
        TenantDbContextFactory tenantFactory,
        IConfiguration config,
        CancellationToken ct)
    {
        // 1. Look up email in central UserTenantIndex for O(1) tenant resolution
        var userIndex = await centralContext.UserTenantIndexes
            .SingleOrDefaultAsync(i => i.Email == request.Email, ct);

        if (userIndex == null)
            return TypedResults.Unauthorized();

        // 2. Load user from tenant database
        var tenantContext = tenantFactory.CreateDbContext(userIndex.TenantId);
        var user = await tenantContext.Users
            .SingleOrDefaultAsync(u => u.Email == request.Email, ct);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return TypedResults.Unauthorized();

        // 3. Generate JWT with tenant_id claim
        var token = GenerateJwt(user, userIndex.TenantId, config);
        var refreshToken = Guid.NewGuid().ToString();

        // 4. Store refresh token in DB (linked to user + tenant)
        var refreshTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            TenantId = userIndex.TenantId,
            Token = BCrypt.Net.BCrypt.HashPassword(refreshToken),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        };
        centralContext.RefreshTokens.Add(refreshTokenEntity);
        await centralContext.SaveChangesAsync(ct);

        // 5. Return JWT; refresh token goes in httpOnly cookie (handled by middleware)
        return TypedResults.Ok(new Response(token));
    }

    private static string GenerateJwt(User user, Guid tenantId, IConfiguration config)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new("tenant_id", tenantId.ToString()),
            new(ClaimTypes.Role, user.Role), // Or multiple roles
        };

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30), // 30 min access token
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

---

## 8. AuthenticationStateProvider for Blazor

**Problem:** Blazor needs to track authentication state across circuit lifetime.

**Solution:** Custom AuthenticationStateProvider with token validation.

```csharp
// IronMonkey.Web/Authentication/AdminAuthenticationStateProvider.cs
namespace IronMonkey.Web.Authentication;

public class AdminAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly ProtectedLocalStorage _storage;
    private readonly ILogger<AdminAuthenticationStateProvider> _logger;

    public AdminAuthenticationStateProvider(
        ProtectedLocalStorage storage,
        ILogger<AdminAuthenticationStateProvider> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var tokenResult = await _storage.GetAsync<string>("auth_token");

            if (!tokenResult.Success || string.IsNullOrEmpty(tokenResult.Value))
            {
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }

            var token = tokenResult.Value;
            var principal = GetPrincipalFromToken(token);

            return new AuthenticationState(principal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading authentication state");
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }
    }

    public async Task LoginAsync(string token, string refreshToken)
    {
        await _storage.SetAsync("auth_token", token);
        await _storage.SetAsync("refresh_token", refreshToken);

        var principal = GetPrincipalFromToken(token);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(principal)));

        _logger.LogInformation("User authenticated");
    }

    public async Task LogoutAsync()
    {
        await _storage.DeleteAsync("auth_token");
        await _storage.DeleteAsync("refresh_token");

        NotifyAuthenticationStateChanged(Task.FromResult(
            new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));

        _logger.LogInformation("User logged out");
    }

    private ClaimsPrincipal GetPrincipalFromToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);

            var claims = jwtToken.Claims.ToList();
            var identity = new ClaimsIdentity(claims, "jwt");
            var principal = new ClaimsPrincipal(identity);

            return principal;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse JWT token");
            return new ClaimsPrincipal(new ClaimsIdentity());
        }
    }
}

// Register in Program.cs:
// builder.Services.AddScoped<AuthenticationStateProvider, AdminAuthenticationStateProvider>();
```

---

## Checklist for Phase 1 Implementation

- [ ] ICircuitHandler implemented and registered; validates user identity on reconnect
- [ ] BearerTokenHandler implemented; attaches JWT to all HTTP requests
- [ ] Token refresh handler intercepts 401; refreshes token and retries
- [ ] DbContext uses AddDbContext (not pooling); verified in startup tests
- [ ] All admin endpoints validate tenant from JWT claims; integration tests verify isolation
- [ ] Tailwind config includes .razor paths; CSS build runs before dotnet build
- [ ] Two-terminal dev setup documented in CLAUDE.md
- [ ] Integration tests cover circuit reconnect, token expiration, tenant isolation
- [ ] Monitoring/logging captures circuit events, token refresh, 401 errors
- [ ] AuthenticationStateProvider manages auth state across circuit lifetime

---

## Performance Baselines to Establish

**Form Rendering:**
- [ ] Single form with 20 fields: < 50ms render time
- [ ] Form with 100 fields: < 200ms render time
- [ ] Form with 200+ fields: Virtualize component required; benchmark after optimization

**Token Refresh:**
- [ ] Token refresh endpoint: < 200ms latency
- [ ] Automatic retry after 401: < 1 sec total (refresh + retry)

**WebSocket:**
- [ ] Form keystroke → validation: < 50 WebSocket messages/sec
- [ ] Large form interaction: < 100KB WebSocket message size

---

## References

- [ASP.NET Core Blazor authentication | Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/)
- [Implementing JWT Token Refresh in DelegatingHandler](https://www.ljblab.dev/blazor-jwt-authentication-deep-dive)
- [Multi-Tenancy with EF Core in Blazor Server | Jeremy Likness Blog](https://blog.jeremylikness.com/blog/multitenancy-with-ef-core-in-blazor-server-apps/)
- [Tailwind CSS v4 Standalone CLI](https://tailwindcss.com/docs/installation/standalone-cli)
