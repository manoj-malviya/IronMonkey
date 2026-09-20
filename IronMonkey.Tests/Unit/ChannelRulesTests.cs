using IronMonkey.Data.Communications;

using Xunit;

namespace IronMonkey.Tests.Unit;

/// <summary>
/// Per-channel capability rules. These encode the differences the prompt calls out — email's
/// subject, SMS segmentation, WhatsApp's template window — so the dispatcher does not have to.
/// </summary>
public class ChannelRulesTests
{
    [Fact]
    public void Only_email_has_a_subject()
    {
        Assert.True(ChannelRules.HasSubject(MessageChannel.Email));
        Assert.False(ChannelRules.HasSubject(MessageChannel.Sms));
        Assert.False(ChannelRules.HasSubject(MessageChannel.WhatsApp));
    }

    [Fact]
    public void Only_email_has_an_html_body()
    {
        Assert.True(ChannelRules.IsHtmlBody(MessageChannel.Email));
        Assert.False(ChannelRules.IsHtmlBody(MessageChannel.Sms));
        Assert.False(ChannelRules.IsHtmlBody(MessageChannel.WhatsApp));
    }

    [Theory]
    [InlineData(0, 1)]     // A blank message is still one send attempt, not zero.
    [InlineData(1, 1)]
    [InlineData(160, 1)]
    // 161 characters costs two segments of 153, not one of 160 plus one of 1 — concatenation
    // headers take their share from every segment. This is the billing surprise the constant
    // exists to prevent.
    [InlineData(161, 2)]
    [InlineData(306, 2)]
    [InlineData(307, 3)]
    public void Segment_count_accounts_for_concatenation_headers(int length, int expected)
    {
        Assert.Equal(expected, ChannelRules.SegmentCount(new string('x', length)));
    }

    [Fact]
    public void An_email_without_a_subject_is_refused()
    {
        Assert.NotNull(ChannelRules.Validate(MessageChannel.Email, subject: null, body: "Hello"));
        Assert.NotNull(ChannelRules.Validate(MessageChannel.Email, subject: "   ", body: "Hello"));
    }

    [Fact]
    public void A_complete_email_passes()
    {
        Assert.Null(ChannelRules.Validate(MessageChannel.Email, "Subject", "Hello"));
    }

    [Fact]
    public void An_empty_body_is_refused_on_every_channel()
    {
        foreach (var channel in Enum.GetValues<MessageChannel>())
        {
            Assert.NotNull(ChannelRules.Validate(channel, "Subject", body: null));
            Assert.NotNull(ChannelRules.Validate(channel, "Subject", body: "   "));
        }
    }

    [Fact]
    public void An_sms_needs_no_subject()
    {
        Assert.Null(ChannelRules.Validate(MessageChannel.Sms, subject: null, body: "Hello"));
    }

    [Fact]
    public void An_sms_over_the_segment_ceiling_is_refused()
    {
        var tooLong = new string('x', ChannelRules.SmsConcatenatedSegmentLength * (ChannelRules.SmsMaxSegments + 1));

        var problem = ChannelRules.Validate(MessageChannel.Sms, null, tooLong);

        Assert.NotNull(problem);
        Assert.Contains("segment", problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WhatsApp_inside_the_service_window_allows_a_free_form_message()
    {
        Assert.Null(ChannelRules.Validate(MessageChannel.WhatsApp, null, "Hello",
            withinServiceWindow: true, hasApprovedTemplate: false));
    }

    [Fact]
    public void WhatsApp_outside_the_window_needs_an_approved_template()
    {
        var problem = ChannelRules.Validate(MessageChannel.WhatsApp, null, "Hello",
            withinServiceWindow: false, hasApprovedTemplate: false);

        Assert.NotNull(problem);
        Assert.Contains("template", problem, StringComparison.OrdinalIgnoreCase);

        Assert.Null(ChannelRules.Validate(MessageChannel.WhatsApp, null, "Hello",
            withinServiceWindow: false, hasApprovedTemplate: true));
    }

    [Fact]
    public void The_whatsapp_service_window_is_twenty_four_hours()
    {
        Assert.Equal(TimeSpan.FromHours(24), ChannelRules.WhatsAppServiceWindow);
    }
}
