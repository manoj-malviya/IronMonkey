using System.Security.Claims;
using IronMonkey.Common.Auth;
using Microsoft.IdentityModel.JsonWebTokens;

namespace IronMonkey.ApiService.Common.Extensions;

internal static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal? principal)
    {
        // JwtBearer leaves MapInboundClaims at its default of true, which rewrites the "sub"
        // claim to ClaimTypes.NameIdentifier before the principal reaches a handler — so
        // looking up "sub" alone finds nothing on an authenticated request and this throws.
        // Jwt.GenerateToken emits both, so fall back to NameIdentifier.
        string? userId = principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(userId, out Guid parsedUserId) ?
            parsedUserId :
            throw new ApplicationException("User id is unavailable");
    }

    public static string GetIdentityId(this ClaimsPrincipal? principal)
    {
        return principal?.FindFirstValue(ClaimTypes.NameIdentifier) ??
               throw new ApplicationException("User identity is unavailable");
    }

    public static Guid? GetTenantId(this ClaimsPrincipal? principal)
    {
        var value = principal?.FindFirst("tenant_id")?.Value;
        return value is not null ? Guid.Parse(value) : null;
    }

    /// <summary>
    /// The platform user behind an impersonation token, or null for an ordinary token.
    /// Present only on tokens minted by <c>POST /admin/tenants/{id}/impersonate</c>.
    /// </summary>
    public static string? GetActAs(this ClaimsPrincipal? principal)
        => principal?.FindFirst(Jwt.ActAsClaim)?.Value;
}