using System.Security.Cryptography;
using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

/// <summary>
/// A link that lets a customer view and respond to one quote without logging in.
///
/// <para>The page it opens is unauthenticated, attacker-reachable surface, so:</para>
/// <list type="bullet">
/// <item><b>Unguessable</b> — 256 random bits from a CSPRNG.</item>
/// <item><b>Stored hashed</b> — only the SHA-256 of the token is persisted, so a database
///   read does not yield working links. The plain token is returned once, at creation.</item>
/// <item><b>Scoped</b> — the hash resolves to exactly one <see cref="QuoteId"/>; nothing on the
///   page is looked up by any other key.</item>
/// <item><b>Expiring and revocable</b> — checked on every request by <see cref="IsUsableAt"/>.</item>
/// </list>
/// </summary>
public sealed class QuoteShareLink : BaseTenantEntity
{
    private QuoteShareLink() { }

    public Guid QuoteId { get; private set; }

    /// <summary>Lower-case hex SHA-256 of the token. Uniquely indexed.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime? LastViewedAt { get; private set; }
    public int ViewCount { get; private set; }

    /// <summary>Creates a link and returns the plain token, which is never stored.</summary>
    public static (QuoteShareLink Link, string Token) Create(Guid tenantId, Guid quoteId, DateTime expiresAtUtc, Guid createdByUserId)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));

        var link = new QuoteShareLink
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            QuoteId = quoteId,
            TokenHash = Hash(token),
            ExpiresAt = DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc),
            CreatedByUserId = createdByUserId
        };

        return (link, token);
    }

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    public bool IsUsableAt(DateTime nowUtc) => RevokedAt is null && nowUtc < ExpiresAt;

    public void Revoke(DateTime nowUtc) => RevokedAt ??= nowUtc;

    public void RecordView(DateTime nowUtc)
    {
        LastViewedAt = nowUtc;
        ViewCount++;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
