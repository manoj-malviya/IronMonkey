using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace IronMonkey.ApiService.Common.Extensions;

internal static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal? principal)
    {
        string? userId = principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);

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
}