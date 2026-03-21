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
    public string GenerateToken(LoggedInUser user)
    {
        var key = SecurityKey(options.Value.Key);
        var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature);
        
        var token = new JwtSecurityToken
        (
            claims: [
                new Claim(ClaimTypes.NameIdentifier, user.IdentityId),
                new Claim(JwtRegisteredClaimNames.Sub, user.IdentityId),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("tenant_id", user.TenantId.ToString())
            ],
            signingCredentials: new(key, SecurityAlgorithms.HmacSha256Signature),
            expires: DateTime.UtcNow.AddYears(1)
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