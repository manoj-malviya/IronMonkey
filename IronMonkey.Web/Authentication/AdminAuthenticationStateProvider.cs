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
                _cachedToken = null;
                _cachedPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
                _initialized = true;
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

    public async Task LogoutAsync()
    {
        try
        {
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
