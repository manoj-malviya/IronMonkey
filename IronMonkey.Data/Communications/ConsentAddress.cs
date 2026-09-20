namespace IronMonkey.Data.Communications;

/// <summary>
/// Normalizes a recipient address into the exact form consent is stored and compared under.
///
/// This exists as one type because opt-out is a correctness boundary with two sides that must
/// agree: the side that <em>writes</em> a suppression (an unsubscribe link, a STOP reply, a
/// bounce) and the side that <em>reads</em> it at send time. If those two normalize
/// differently by even a space or a leading zero, the lookup misses and the system messages
/// someone who asked it not to.
/// </summary>
public static class ConsentAddress
{
    /// <summary>
    /// The storage form of an address for the given channel.
    ///
    /// Email is lowercased and trimmed — mail domains are case-insensitive, and a customer
    /// unsubscribing from a link in an email routinely arrives with different casing than the
    /// address the CRM holds.
    ///
    /// Phone numbers keep digits only, with a leading "+" dropped. Formatting is cosmetic and
    /// wildly inconsistent in imported CRM data: "+1 (555) 010-9999", "15550109999" and
    /// "+1-555-010-9999" are one person, and treating them as three would mean one STOP
    /// suppresses only one of them.
    /// </summary>
    public static string Normalize(MessageChannel channel, string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return string.Empty;

        return channel == MessageChannel.Email
            ? address.Trim().ToLowerInvariant()
            : DigitsOnly(address);
    }

    private static string DigitsOnly(string value)
    {
        Span<char> buffer = value.Length <= 64 ? stackalloc char[value.Length] : new char[value.Length];
        var length = 0;

        foreach (var c in value)
        {
            if (char.IsAsciiDigit(c))
                buffer[length++] = c;
        }

        return new string(buffer[..length]);
    }
}
