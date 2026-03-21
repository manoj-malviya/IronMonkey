using Xunit;

namespace IronMonkey.Tests.Unit;

public class ApiAuthTests
{
    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public void GenerateApiKey_ReturnsBase64String_Not_Plaintext_Hash() { }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public void VerifyApiKey_WithCorrectKey_ReturnsTrue() { }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public void VerifyApiKey_WithWrongKey_ReturnsFalse() { }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public void VerifyApiKey_WithEmptyKey_ReturnsFalse() { }
}
