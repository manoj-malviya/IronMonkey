using IronMonkey.Data.Communications;

using Xunit;

namespace IronMonkey.Tests.Unit;

/// <summary>
/// Address normalization.
///
/// This is a correctness boundary, not a formatting nicety: the value written when someone
/// opts out and the value checked when a message is about to go must agree. If they diverge,
/// the suppression lookup misses and the system messages someone who asked it not to.
/// </summary>
public class ConsentAddressTests
{
    [Theory]
    [InlineData("Ada@Example.COM", "ada@example.com")]
    [InlineData("  ada@example.com  ", "ada@example.com")]
    [InlineData("ada@example.com", "ada@example.com")]
    public void Email_is_lowercased_and_trimmed(string input, string expected)
    {
        Assert.Equal(expected, ConsentAddress.Normalize(MessageChannel.Email, input));
    }

    [Theory]
    [InlineData("+1 (555) 010-9999")]
    [InlineData("15550109999")]
    [InlineData("+1-555-010-9999")]
    [InlineData("  +1 555 010 9999  ")]
    public void Every_formatting_of_one_number_normalizes_to_the_same_value(string input)
    {
        // Imported CRM data punctuates numbers inconsistently. Treating these as different
        // people would mean one STOP suppresses only one of them.
        Assert.Equal("15550109999", ConsentAddress.Normalize(MessageChannel.Sms, input));
    }

    [Fact]
    public void WhatsApp_numbers_normalize_the_same_way_as_sms()
    {
        Assert.Equal(
            ConsentAddress.Normalize(MessageChannel.Sms, "+1 (555) 010-9999"),
            ConsentAddress.Normalize(MessageChannel.WhatsApp, "+1 (555) 010-9999"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_address_normalizes_to_empty(string? input)
    {
        Assert.Equal(string.Empty, ConsentAddress.Normalize(MessageChannel.Email, input));
        Assert.Equal(string.Empty, ConsentAddress.Normalize(MessageChannel.Sms, input));
    }

    [Fact]
    public void A_phone_value_with_no_digits_normalizes_to_empty()
    {
        Assert.Equal(string.Empty, ConsentAddress.Normalize(MessageChannel.Sms, "not-a-number"));
    }

    [Fact]
    public void Email_normalization_does_not_strip_punctuation()
    {
        // The phone rule must not leak into email: stripping non-digits would reduce every
        // address to its digits and collapse unrelated people together.
        Assert.Equal("ada.lovelace+crm@example.com",
            ConsentAddress.Normalize(MessageChannel.Email, "Ada.Lovelace+CRM@Example.com"));
    }
}
