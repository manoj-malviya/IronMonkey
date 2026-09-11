using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace IronMonkey.Web.Authentication;

public class AdminAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly ProtectedSessionStorage _sessionStorage;
    private readonly ILogger<AdminAuthenticationStateProvider> _logger;
    private readonly NavigationManager _nav;
    private ClaimsPrincipal? _cachedPrincipal;
    private bool _initialized = false;

    // Session storage is only reachable through JS interop, which is unavailable during
    // prerendering. Cache the token in the circuit so callers on the request path (e.g.
    // BearerTokenHandler) can attach it without triggering another interop call.
    private string? _cachedToken;

    /// <summary>
    /// The current JWT, or null if unknown. Never performs JS interop, so it is safe to
    /// call while prerendering — it returns null rather than throwing.
    /// </summary>
    public string? CachedToken => _cachedToken;

    /// <summary>Session-storage slot holding the platform token while impersonating.</summary>
    private const string PlatformTokenKey = "platform_auth_token";

    /// <summary>
    /// True while the current token was minted by impersonation, i.e. it carries an act_as
    /// claim. Drives the warning banner and the "exit impersonation" affordance.
    /// </summary>
    public bool IsImpersonating =>
        _cachedPrincipal?.FindFirst("act_as")?.Value is not null;

    /// <summary>The tenant name being impersonated, for display. Null when not impersonating.</summary>
    public string? ImpersonatedTenantName { get; private set; }

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

        // During prerendering, ProtectedSessionStorage may throw because JS interop is not ready.
        // Return anonymous state for prerender and allow later initialization after first render.
        try
        {
            var tokenResult = await _sessionStorage.GetAsync<string>("auth_token");

            if (!tokenResult.Success || string.IsNullOrEmpty(tokenResult.Value))
            {
                // Do NOT latch _initialized here. During prerender this call does not throw —
                // it returns Success:false because JS interop is unavailable, which is
                // indistinguishable from "no token stored". Marking the provider initialized
                // on that result permanently caches an anonymous principal for the circuit,
                // so InitializeAsync short-circuits after the first render and every API call
                // goes out unauthenticated. Leaving it unset lets the post-render pass retry.
                _cachedToken = null;
                _cachedPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
                return new AuthenticationState(_cachedPrincipal);
            }

            _cachedToken = tokenResult.Value;
            var principal = ValidateAndGetPrincipal(tokenResult.Value);
            _cachedPrincipal = principal;
            _initialized = true;
            return new AuthenticationState(principal);
        }
        catch (InvalidOperationException)
        {
            // JS is not available during prerender. Defer reading until OnAfterRenderAsync.
            _cachedPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
            return new AuthenticationState(_cachedPrincipal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading authentication state from session storage");
            _cachedPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
            _initialized = true;
            return new AuthenticationState(_cachedPrincipal);
        }
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        try
        {
            var tokenResult = await _sessionStorage.GetAsync<string>("auth_token");

            if (!tokenResult.Success || string.IsNullOrEmpty(tokenResult.Value))
            {
                _cachedToken = null;
                _cachedPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
            }
            else
            {
                _cachedToken = tokenResult.Value;
                _cachedPrincipal = ValidateAndGetPrincipal(tokenResult.Value);
            }

            // Reached only from OnAfterRenderAsync, where interop is genuinely available,
            // so an empty result here really does mean "not logged in" and is safe to latch.
            _initialized = true;
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_cachedPrincipal)));
        }
        catch (InvalidOperationException)
        {
            // Still prerendering or JS not ready; skip and try again later.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing authentication state");
            _cachedPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
            _initialized = true;
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_cachedPrincipal)));
        }
    }

    public async Task LoginAsync(string token)
    {
        try
        {
            await _sessionStorage.SetAsync("auth_token", token);
            _cachedToken = token;
            var principal = ValidateAndGetPrincipal(token);
            _cachedPrincipal = principal;
            _initialized = true;
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(principal)));
            _logger.LogInformation("User authenticated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during LoginAsync");
            throw;
        }
    }

    /// <summary>
    /// Switches the circuit to an impersonation token, stashing the platform token so the
    /// operator can return to the platform console. The impersonation token is short-lived
    /// and stateless: exiting simply restores the stashed token — the server has nothing to
    /// revoke, so no round trip is needed.
    /// </summary>
    public async Task ImpersonateAsync(string impersonationToken, string tenantName)
    {
        var platformToken = await GetTokenAsync();

        if (!string.IsNullOrEmpty(platformToken))
            await _sessionStorage.SetAsync(PlatformTokenKey, platformToken);

        ImpersonatedTenantName = tenantName;
        await LoginAsync(impersonationToken);

        _logger.LogInformation("Started impersonating tenant {TenantName}", tenantName);
    }

    /// <summary>
    /// Restores the stashed platform token. Falls back to a full logout when none is held —
    /// without it the circuit would keep a tenant identity the operator cannot leave.
    /// </summary>
    public async Task StopImpersonatingAsync()
    {
        var platformToken = await ReadPlatformTokenAsync();

        ImpersonatedTenantName = null;
        await _sessionStorage.DeleteAsync(PlatformTokenKey);

        if (string.IsNullOrEmpty(platformToken))
        {
            _logger.LogWarning("No platform token stashed; logging out instead of restoring.");
            await LogoutAsync();
            return;
        }

        await LoginAsync(platformToken);
        _logger.LogInformation("Stopped impersonating; platform session restored.");
        _nav.NavigateTo("/admin/tenants", forceLoad: false);
    }

    private async Task<string?> ReadPlatformTokenAsync()
    {
        try
        {
            var result = await _sessionStorage.GetAsync<string>(PlatformTokenKey);
            return result.Success ? result.Value : null;
        }
        catch
        {
            // JS interop unavailable (prerender) — treat as "nothing stashed".
            return null;
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            ImpersonatedTenantName = null;
            await _sessionStorage.DeleteAsync(PlatformTokenKey);
            await _sessionStorage.DeleteAsync("auth_token");
            _cachedToken = null;
            _cachedPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
            _initialized = false;
            NotifyAuthenticationStateChanged(Task.FromResult(
                new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
            _logger.LogInformation("User logged out");
            _nav.NavigateTo("/login", forceLoad: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during LogoutAsync");
        }
    }

    public async Task<string?> GetTokenAsync()
    {
        if (_cachedToken is not null)
            return _cachedToken;

        try
        {
            var result = await _sessionStorage.GetAsync<string>("auth_token");
            if (result.Success && !string.IsNullOrEmpty(result.Value))
                _cachedToken = result.Value;

            return _cachedToken;
        }
        catch
        {
            // JS interop unavailable (prerender). Caller treats null as "not authenticated".
            return null;
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

            if (jwtToken.ValidTo < DateTime.UtcNow)
            {
                _logger.LogWarning("JWT token expired at {ExpiresAt}", jwtToken.ValidTo);
                return new ClaimsPrincipal(new ClaimsIdentity());
            }

            var claims = jwtToken.Claims.ToList();
            // Name/role claim types are stated explicitly rather than left to the
            // constructor default: the PlatformAdmin policy resolves the role through
            // RoleClaimType, so a silent default change here would fail open on every
            // role-gated page. Jwt.GenerateToken emits the matching ClaimTypes values.
            var identity = new ClaimsIdentity(claims, "jwt", ClaimTypes.Name, ClaimTypes.Role);
            return new ClaimsPrincipal(identity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse JWT token");
            return new ClaimsPrincipal(new ClaimsIdentity());
        }
    }
}
