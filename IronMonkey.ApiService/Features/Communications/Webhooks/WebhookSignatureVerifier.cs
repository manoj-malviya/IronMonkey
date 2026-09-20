using System.Security.Cryptography;
using System.Text;

namespace IronMonkey.ApiService.Features.Communications.Webhooks;

/// <summary>
/// Verifies provider webhook signatures.
///
/// <para>A webhook endpoint is unauthenticated by necessity — the provider has no session —
/// so the signature <em>is</em> the authentication. Without it, anyone who learns the URL can
/// post a "delivered" receipt for a message that bounced, or inject a fabricated inbound
/// message attributed to a customer.</para>
///
/// <para>Comparison is fixed-time. A byte-by-byte comparison that returns early leaks, through
/// timing, how much of a guessed signature was correct, which turns forgery from infeasible
/// into a few thousand requests.</para>
/// </summary>
public static class WebhookSignatureVerifier
{
    /// <summary>
    /// How far a timestamp may be from now before the callback is treated as a replay.
    ///
    /// Signatures do not expire on their own, so a captured valid callback could otherwise be
    /// resubmitted forever — repeatedly marking a message delivered, or replaying an inbound
    /// message into the timeline.
    /// </summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Twilio's scheme: HMAC-SHA1 over the full request URL with POST parameters appended in
    /// sorted key order, keyed by the account auth token, base64-encoded.
    /// </summary>
    public static bool VerifyTwilio(string? authToken, string url, IEnumerable<KeyValuePair<string, string>> form, string? providedSignature)
    {
        // No configured token means the signature cannot be verified. Refusing is the only
        // safe answer: accepting unverifiable callbacks would make the endpoint an open write
        // into tenant data.
        if (string.IsNullOrWhiteSpace(authToken) || string.IsNullOrWhiteSpace(providedSignature))
            return false;

        var builder = new StringBuilder(url);

        foreach (var pair in form.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key);
            builder.Append(pair.Value);
        }

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(authToken));
        var computed = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));

        return FixedTimeEquals(computed, providedSignature);
    }

    /// <summary>
    /// The generic scheme used for the built-in/no-op inbound endpoint and any provider
    /// following the common "t=timestamp,v1=hex-hmac-sha256" convention over
    /// <c>{timestamp}.{body}</c>.
    /// </summary>
    public static bool VerifyHmacSha256(string? secret, string timestamp, string body, string? providedSignature,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(providedSignature))
            return false;

        if (!long.TryParse(timestamp, out var unixSeconds))
            return false;

        // Replay window. Checked before the HMAC so a stale-but-valid capture is rejected on
        // its age rather than accepted on its signature.
        var sent = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (Abs(now - sent) > MaxClockSkew)
            return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();

        return FixedTimeEquals(computed, providedSignature.Trim());
    }

    private static TimeSpan Abs(TimeSpan value) => value < TimeSpan.Zero ? -value : value;

    /// <summary>
    /// Compares two signatures without leaking their divergence point through timing.
    ///
    /// Length is compared first and separately — that is unavoidable and harmless, since a
    /// signature's length is fixed and public.
    /// </summary>
    private static bool FixedTimeEquals(string computed, string provided)
    {
        var a = Encoding.UTF8.GetBytes(computed);
        var b = Encoding.UTF8.GetBytes(provided);

        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
