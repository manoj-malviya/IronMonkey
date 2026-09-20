namespace IronMonkey.Data.Communications;

/// <summary>
/// The per-channel rules that differ between email, SMS and WhatsApp.
///
/// These live in one place rather than inside each provider because they are properties of
/// the <em>channel</em>, not of the vendor carrying it: an SMS is split into 153-character
/// segments whether Twilio or anyone else delivers it, and a tenant needs to know the cost
/// of a message before choosing a provider. Putting them in the provider would also mean the
/// no-op development provider silently accepted messages the real one would reject.
/// </summary>
public static class ChannelRules
{
    /// <summary>A single GSM-7 SMS. Beyond this the message is split and billed per segment.</summary>
    public const int SmsSingleSegmentLength = 160;

    /// <summary>
    /// Each segment of a multi-part SMS, once concatenation headers take their share. This is
    /// why a 161-character message costs two segments of 153, not one of 160 plus one of 1.
    /// </summary>
    public const int SmsConcatenatedSegmentLength = 153;

    /// <summary>
    /// Ceiling on a single SMS send. Carriers cap concatenation, and a template that runs
    /// away — a placeholder resolving to a pasted document — would otherwise become a large
    /// and surprising bill.
    /// </summary>
    public const int SmsMaxSegments = 10;

    /// <summary>
    /// WhatsApp's customer-service window. Inside it a business may send free-form messages;
    /// outside it, only pre-approved templates are accepted. Measured from the customer's
    /// most recent inbound message.
    /// </summary>
    public static readonly TimeSpan WhatsAppServiceWindow = TimeSpan.FromHours(24);

    /// <summary>Whether this channel carries a subject line.</summary>
    public static bool HasSubject(MessageChannel channel) => channel == MessageChannel.Email;

    /// <summary>Whether this channel's body is HTML rather than plain text.</summary>
    public static bool IsHtmlBody(MessageChannel channel) => channel == MessageChannel.Email;

    /// <summary>
    /// How many SMS segments a body occupies. Returns 1 for empty input — a blank message is
    /// still one send attempt, and returning 0 would make it look free.
    /// </summary>
    public static int SegmentCount(string? body)
    {
        var length = body?.Length ?? 0;
        if (length == 0) return 1;
        if (length <= SmsSingleSegmentLength) return 1;

        return (int)Math.Ceiling(length / (double)SmsConcatenatedSegmentLength);
    }

    /// <summary>
    /// Validates a message against its channel's rules, returning the reason it cannot be
    /// sent or null when it can.
    ///
    /// <paramref name="withinServiceWindow"/> is only consulted for WhatsApp, and
    /// <paramref name="hasApprovedTemplate"/> only matters when outside that window.
    /// </summary>
    public static string? Validate(
        MessageChannel channel,
        string? subject,
        string? body,
        bool withinServiceWindow = true,
        bool hasApprovedTemplate = false)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "The message body is empty.";

        switch (channel)
        {
            case MessageChannel.Email:
                if (string.IsNullOrWhiteSpace(subject))
                    return "An email needs a subject.";
                break;

            case MessageChannel.Sms:
                var segments = SegmentCount(body);
                if (segments > SmsMaxSegments)
                    return $"The message is {segments} SMS segments, over the {SmsMaxSegments}-segment limit.";
                break;

            case MessageChannel.WhatsApp:
                if (!withinServiceWindow && !hasApprovedTemplate)
                    return "Outside the 24-hour customer-service window, WhatsApp accepts only pre-approved templates.";
                break;
        }

        return null;
    }
}
