using System.Security.Cryptography;
using System.Text;
using IronMonkey.ApiService.Features.Communications.Webhooks;

using Xunit;

namespace IronMonkey.Tests.Unit;

/// <summary>
/// Webhook signature verification.
///
/// A webhook endpoint is unauthenticated by necessity, so the signature is the only thing
/// standing between a provider callback and an attacker writing into tenant data — posting a
/// fake "delivered" receipt, or injecting a fabricated inbound message attributed to a
/// customer. These tests pin that an unsigned, mis-signed, or replayed callback is refused.
/// </summary>
public class WebhookSignatureTests
{
    private const string AuthToken = "test-auth-token";
    private const string Url = "https://crm.example.com/api/webhooks/twilio/abc123";

    private static string SignTwilio(string token, string url, IEnumerable<KeyValuePair<string, string>> form)
    {
        var builder = new StringBuilder(url);
        foreach (var pair in form.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key);
            builder.Append(pair.Value);
        }

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static List<KeyValuePair<string, string>> Form() =>
    [
        new("From", "+15550109999"),
        new("To", "+15550100000"),
        new("Body", "Hello")
    ];

    [Fact]
    public void A_correctly_signed_twilio_callback_is_accepted()
    {
        var form = Form();
        var signature = SignTwilio(AuthToken, Url, form);

        Assert.True(WebhookSignatureVerifier.VerifyTwilio(AuthToken, Url, form, signature));
    }

    [Fact]
    public void Parameter_order_does_not_change_the_result()
    {
        // Twilio sorts by key before signing, so a form arriving in a different order must
        // still verify — otherwise valid callbacks would be rejected at random.
        var signature = SignTwilio(AuthToken, Url, Form());
        var reordered = Form().AsEnumerable().Reverse().ToList();

        Assert.True(WebhookSignatureVerifier.VerifyTwilio(AuthToken, Url, reordered, signature));
    }

    [Fact]
    public void An_unsigned_callback_is_rejected()
    {
        Assert.False(WebhookSignatureVerifier.VerifyTwilio(AuthToken, Url, Form(), null));
        Assert.False(WebhookSignatureVerifier.VerifyTwilio(AuthToken, Url, Form(), ""));
    }

    [Fact]
    public void A_forged_signature_is_rejected()
    {
        Assert.False(WebhookSignatureVerifier.VerifyTwilio(AuthToken, Url, Form(), "not-a-real-signature"));
    }

    [Fact]
    public void A_signature_from_a_different_token_is_rejected()
    {
        var signature = SignTwilio("someone-elses-token", Url, Form());

        Assert.False(WebhookSignatureVerifier.VerifyTwilio(AuthToken, Url, Form(), signature));
    }

    [Fact]
    public void A_tampered_body_invalidates_the_signature()
    {
        var signature = SignTwilio(AuthToken, Url, Form());

        var tampered = Form();
        tampered[2] = new KeyValuePair<string, string>("Body", "Hello, and also transfer money");

        Assert.False(WebhookSignatureVerifier.VerifyTwilio(AuthToken, Url, tampered, signature));
    }

    [Fact]
    public void A_signature_for_a_different_url_is_rejected()
    {
        // The URL is part of what is signed, so a callback captured for one tenant's routing
        // token cannot be replayed against another's.
        var signature = SignTwilio(AuthToken, "https://crm.example.com/api/webhooks/twilio/other", Form());

        Assert.False(WebhookSignatureVerifier.VerifyTwilio(AuthToken, Url, Form(), signature));
    }

    [Fact]
    public void Without_a_configured_token_nothing_verifies()
    {
        // An unconfigured provider must refuse callbacks rather than accept them unverified,
        // which would make the endpoint an open write into tenant data.
        Assert.False(WebhookSignatureVerifier.VerifyTwilio(null, Url, Form(), "anything"));
        Assert.False(WebhookSignatureVerifier.VerifyTwilio("", Url, Form(), "anything"));
    }

    private static string SignHmac(string secret, string timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();
    }

    [Fact]
    public void A_correctly_signed_generic_callback_is_accepted()
    {
        var now = DateTimeOffset.UtcNow;
        var timestamp = now.ToUnixTimeSeconds().ToString();
        const string body = """{"channel":"Email","from":"ada@example.com"}""";

        var signature = SignHmac("secret", timestamp, body);

        Assert.True(WebhookSignatureVerifier.VerifyHmacSha256("secret", timestamp, body, signature, now));
    }

    [Fact]
    public void A_replayed_callback_is_rejected_on_its_age()
    {
        // The signature is still valid — this is a genuine captured callback. It is refused
        // because it is old, which is what stops a captured receipt being resubmitted forever.
        var now = DateTimeOffset.UtcNow;
        var stale = now - WebhookSignatureVerifier.MaxClockSkew - TimeSpan.FromMinutes(1);
        var timestamp = stale.ToUnixTimeSeconds().ToString();
        const string body = """{"channel":"Email"}""";

        var signature = SignHmac("secret", timestamp, body);

        Assert.False(WebhookSignatureVerifier.VerifyHmacSha256("secret", timestamp, body, signature, now));
    }

    [Fact]
    public void A_callback_from_the_future_is_also_rejected()
    {
        // Clock skew is bounded in both directions; an unbounded future timestamp would let a
        // forger mint a callback that stays valid indefinitely.
        var now = DateTimeOffset.UtcNow;
        var future = now + WebhookSignatureVerifier.MaxClockSkew + TimeSpan.FromMinutes(1);
        var timestamp = future.ToUnixTimeSeconds().ToString();
        const string body = """{"channel":"Email"}""";

        var signature = SignHmac("secret", timestamp, body);

        Assert.False(WebhookSignatureVerifier.VerifyHmacSha256("secret", timestamp, body, signature, now));
    }

    [Fact]
    public void A_tampered_generic_body_invalidates_the_signature()
    {
        var now = DateTimeOffset.UtcNow;
        var timestamp = now.ToUnixTimeSeconds().ToString();

        var signature = SignHmac("secret", timestamp, """{"channel":"Email"}""");

        Assert.False(WebhookSignatureVerifier.VerifyHmacSha256(
            "secret", timestamp, """{"channel":"Sms"}""", signature, now));
    }

    [Fact]
    public void A_malformed_timestamp_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.False(WebhookSignatureVerifier.VerifyHmacSha256(
            "secret", "not-a-timestamp", "body", SignHmac("secret", "not-a-timestamp", "body"), now));
    }

    [Fact]
    public void Without_a_configured_secret_nothing_verifies()
    {
        var now = DateTimeOffset.UtcNow;
        var timestamp = now.ToUnixTimeSeconds().ToString();

        Assert.False(WebhookSignatureVerifier.VerifyHmacSha256(null, timestamp, "body", "sig", now));
        Assert.False(WebhookSignatureVerifier.VerifyHmacSha256("", timestamp, "body", "sig", now));
    }
}
