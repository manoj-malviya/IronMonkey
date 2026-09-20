using System.Security.Cryptography;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.ApiService.Features.UserManagement.Invitations;

/// <summary>A freshly minted token: the plaintext exists only here and in the response.</summary>
public sealed record IssuedToken(string Plaintext, string Hash, string Prefix);

/// <summary>
/// Mints and verifies invitation tokens.
///
/// Only the BCrypt hash is ever persisted (the <see cref="IronMonkey.Data.Entities.ApiKey"/>
/// precedent), so the table is not a list of working invitations. BCrypt's verify is itself
/// constant-time for a given hash, which is what removes the timing signal on the secret
/// part; the prefix used to find the row is not secret.
/// </summary>
public static class InvitationToken
{
    /// <summary>
    /// How long a link stays usable. Long enough to survive a weekend and a spam folder,
    /// short enough that an old forwarded mail is not a standing door into the tenant.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public const int PrefixLength = 8;

    public static IssuedToken Issue()
    {
        // 32 bytes of CSPRNG output, URL-safe so it survives being pasted into a link
        // without escaping. Well beyond guessing range, which matters because the prefix
        // narrows a lookup but the remainder is the whole secret.
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);

        var plaintext = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        return new IssuedToken(plaintext, BC.HashPassword(plaintext), plaintext[..PrefixLength]);
    }

    /// <summary>
    /// True when <paramref name="plaintext"/> is the token behind <paramref name="hash"/>.
    ///
    /// An empty or malformed stored hash returns false rather than throwing: Revoke and
    /// Accept both clear the hash, and that clearing is meant to be a refusal, not a 500.
    /// </summary>
    public static bool Verify(string plaintext, string hash)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(plaintext))
            return false;

        try
        {
            return BC.Verify(plaintext, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    /// <summary>Safe prefix extraction for a caller-supplied token of unknown length.</summary>
    public static string? PrefixOf(string? plaintext)
        => string.IsNullOrWhiteSpace(plaintext) || plaintext.Length < PrefixLength
            ? null
            : plaintext[..PrefixLength];
}
