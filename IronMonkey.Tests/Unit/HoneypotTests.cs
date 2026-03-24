using Xunit;

namespace IronMonkey.Tests.Unit;

public class HoneypotTests
{
    // The honeypot logic: if Website field is non-null and non-empty (including whitespace) → bot detected
    // Whitespace-only is also treated as bot — auto-fill scripts may inject spaces
    private static bool IsBot(string? websiteField)
        => websiteField != null && websiteField.Length > 0;

    [Fact]
    public void IsBot_WhenHoneypotFieldIsEmpty_ReturnsFalse()
    {
        Assert.False(IsBot(null));
        Assert.False(IsBot(""));
    }

    [Fact]
    public void IsBot_WhenHoneypotFieldHasValue_ReturnsTrue()
    {
        Assert.True(IsBot("http://spam.com"));
        Assert.True(IsBot("anything"));
    }

    [Fact]
    public void IsBot_WhenHoneypotFieldIsWhitespace_ReturnsTrue()
    {
        // Whitespace only should still be treated as bot (auto-filled)
        Assert.True(IsBot("   "));
    }
}
