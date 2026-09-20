using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Communications.Webhooks;

public interface IWebhookTenantResolver
{
    /// <summary>
    /// Resolves the tenant a callback belongs to from its routing token, or null when the
    /// token matches no tenant.
    /// </summary>
    Task<Guid?> ResolveAsync(string? routingToken, CancellationToken cancellationToken);

    /// <summary>The routing token for a tenant, for building the URL given to the provider.</summary>
    string TokenFor(Guid tenantId);
}

/// <summary>
/// Maps an inbound callback to a tenant.
///
/// <para><b>The tenant is never read from the request body.</b> A webhook endpoint is
/// unauthenticated, so a tenant id in the payload is attacker-supplied: anyone could post a
/// message into any tenant's timeline, or mark another tenant's messages delivered. The tenant
/// comes from the routing token in the URL path, which is issued per tenant and given only to
/// the provider.</para>
///
/// <para>The token is derived from the tenant id and a server secret rather than stored,
/// so there is no table to keep in sync and no way to enumerate tenants from one token. It is
/// a routing identifier, not an authenticator — the signature check is what authenticates the
/// caller — but deriving it keeps a guessed token from resolving to a real tenant.</para>
/// </summary>
public sealed class WebhookTenantResolver(
    CentralDbContext centralDb,
    IConfiguration configuration) : IWebhookTenantResolver
{
    /// <summary>
    /// Falls back to a fixed development value when unset, so local runs work. That is safe
    /// because the token only selects a tenant; a forged callback still has to produce a valid
    /// provider signature to be accepted.
    /// </summary>
    private string Secret => configuration["Communications:WebhookRoutingSecret"]
                             ?? "ironmonkey-development-webhook-routing";

    public async Task<Guid?> ResolveAsync(string? routingToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(routingToken)) return null;

        var trimmed = routingToken.Trim();

        // Every provisioned tenant is checked against the token rather than the token being
        // reversed, because the derivation is one-way. The tenant list is small and cached by
        // EF for the request; a deployment with very many tenants would index the token
        // instead.
        var tenantIds = await centralDb.Tenants
            .AsNoTracking()
            .Where(t => t.IsProvisioned)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        foreach (var tenantId in tenantIds)
        {
            if (CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(TokenFor(tenantId)),
                    System.Text.Encoding.UTF8.GetBytes(trimmed)))
            {
                return tenantId;
            }
        }

        return null;
    }

    public string TokenFor(Guid tenantId)
    {
        using var hmac = new HMACSHA256(System.Text.Encoding.UTF8.GetBytes(Secret));
        var hash = hmac.ComputeHash(tenantId.ToByteArray());

        // URL-safe, and truncated to 128 bits — ample against guessing while keeping the
        // callback URL something a human can paste into a provider console.
        return Convert.ToBase64String(hash[..16])
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
