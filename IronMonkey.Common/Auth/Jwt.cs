using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IronMonkey.Common.Auth;

public class JwtOptions
{
    public required string Key { get; init; }
}

public class Jwt(IOptions<JwtOptions> options)
{
    /// <summary>The claim naming the platform user behind an impersonation token.</summary>
    public const string ActAsClaim = "act_as";

    /// <param name="lifetime">
    /// How long the token stays valid. Defaults to the standard login lifetime; impersonation
    /// tokens pass a short span so a borrowed tenant identity expires on its own.
    /// </param>
    /// <param name="actAs">
    /// The platform user's id when this token was minted by impersonation. Purely a marker:
    /// it records who is really acting, and blocks an impersonation token from minting another.
    /// </param>
    public string GenerateToken(LoggedInUser user, TimeSpan? lifetime = null, string? actAs = null)
    {
        var key = SecurityKey(options.Value.Key);
        var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.IdentityId),
            new(JwtRegisteredClaimNames.Sub, user.IdentityId),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Role, user.Role),
            new("tenant_id", user.TenantId.ToString())
        };

        if (!string.IsNullOrEmpty(actAs))
            claims.Add(new Claim(ActAsClaim, actAs));

        var token = new JwtSecurityToken
        (
            claims: claims,
            signingCredentials: new(key, SecurityAlgorithms.HmacSha256Signature),
            expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromDays(365))
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    
    public ClaimsPrincipal ValidateToken(string token)
    {
        var key = SecurityKey(options.Value.Key);
        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        return tokenHandler.ValidateToken(token, validationParameters, out _);
    }
    
    public LoggedInUser GetUserFromToken(string token)
    {
        var claims = ValidateToken(token).Claims;
        var identityId = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        var name = claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value ?? string.Empty;
        var email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value ?? string.Empty;
        var role = claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value ?? string.Empty;
        var tenantIdStr = claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value;
        var tenantId = tenantIdStr is not null ? Guid.Parse(tenantIdStr) : Guid.Empty;

        return new LoggedInUser(identityId, name, email, role, tenantId);
    }

    public static SymmetricSecurityKey SecurityKey(string key) => new(Encoding.ASCII.GetBytes(key));
}