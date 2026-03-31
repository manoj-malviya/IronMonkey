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

            var principal = ValidateAndGetPrincipal(tokenResult.Value);
            _cachedPrincipal = principal;
            _initialized = true;
            return new AuthenticationState(principal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading authentication state from session storage");
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
        try
        {
            var result = await _sessionStorage.GetAsync<string>("auth_token");
            return result.Success ? result.Value : null;
        }
        catch
        {
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
