using Xunit;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.Tests.Unit;

public class ApiAuthTests
{
    [Fact]
    public void GenerateApiKey_ReturnsBase64String_Not_Plaintext_Hash()
    {
        // Simulate the key generation logic from ApiKeyService
        var keyBytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes);
        var plaintextKey = Convert.ToBase64String(keyBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // Key should be base64-like, not a BCrypt hash (BCrypt hashes start with $2)
        Assert.False(plaintextKey.StartsWith("$2"), "Plaintext key should not be a BCrypt hash");
        Assert.True(plaintextKey.Length > 10, "Key should be reasonably long");
    }

    [Fact]
    public void VerifyApiKey_WithCorrectKey_ReturnsTrue()
    {
        var plaintextKey = "test-api-key-12345";
        var hash = BC.HashPassword(plaintextKey, workFactor: 4);  // Low work factor for test speed

        var result = BC.Verify(plaintextKey, hash);

        Assert.True(result);
    }

    [Fact]
    public void VerifyApiKey_WithWrongKey_ReturnsFalse()
    {
        var plaintextKey = "test-api-key-12345";
        var hash = BC.HashPassword(plaintextKey, workFactor: 4);

        var result = BC.Verify("wrong-key", hash);

        Assert.False(result);
    }

    [Fact]
    public void VerifyApiKey_WithEmptyKey_ReturnsFalse()
    {
        var hash = BC.HashPassword("real-key", workFactor: 4);

        // Empty string should not match
        var result = BC.Verify("", hash);

        Assert.False(result);
    }
}
