using IronMonkey.Data.Communications;

using Xunit;

namespace IronMonkey.Tests.Unit;

/// <summary>
/// Template rendering, with the escaping behaviour pinned.
///
/// The escaping tests are the important ones: a lead's name arrives from a public web form,
/// so it is attacker-controlled, and it is rendered into an HTML email and back into the
/// tenant's own browser on the timeline. If escaping regresses, both become injection points.
/// </summary>
public class MessageTemplateRendererTests
{
    private static Dictionary<string, string?> Values(params (string Key, string? Value)[] pairs)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pairs) values[key] = value;
        return values;
    }

    [Fact]
    public void Substitutes_known_placeholders()
    {
        var result = MessageTemplateRenderer.RenderText(
            "Hello {{FirstName}} {{LastName}}", Values(("FirstName", "Ada"), ("LastName", "Lovelace")));

        Assert.True(result.Succeeded);
        Assert.Equal("Hello Ada Lovelace", result.Text);
    }

    [Fact]
    public void Tolerates_whitespace_inside_the_braces()
    {
        var result = MessageTemplateRenderer.RenderText("Hi {{  FirstName  }}", Values(("FirstName", "Ada")));

        Assert.True(result.Succeeded);
        Assert.Equal("Hi Ada", result.Text);
    }

    [Fact]
    public void Placeholder_names_are_case_insensitive()
    {
        var result = MessageTemplateRenderer.RenderText("Hi {{firstname}}", Values(("FirstName", "Ada")));

        Assert.True(result.Succeeded);
        Assert.Equal("Hi Ada", result.Text);
    }

    [Fact]
    public void Html_rendering_escapes_a_hostile_lead_name()
    {
        // A lead created through the public web form can be called anything. This is the
        // exact payload that would otherwise execute in a recipient's mail client and in the
        // tenant's own timeline view.
        var result = MessageTemplateRenderer.RenderHtml(
            "<p>Hello {{FirstName}}</p>",
            Values(("FirstName", "<script>alert('xss')</script>")));

        Assert.True(result.Succeeded);
        Assert.DoesNotContain("<script>", result.Text);
        Assert.Contains("&lt;script&gt;", result.Text);
    }

    [Fact]
    public void Html_rendering_escapes_attribute_breaking_characters()
    {
        var result = MessageTemplateRenderer.RenderHtml(
            "<a title=\"{{FirstName}}\">x</a>",
            Values(("FirstName", "\" onmouseover=\"alert(1)")));

        Assert.True(result.Succeeded);
        Assert.DoesNotContain("onmouseover=\"alert(1)\"", result.Text);
        Assert.Contains("&quot;", result.Text);
    }

    [Fact]
    public void Html_rendering_preserves_the_templates_own_markup()
    {
        // The template author is a trusted tenant Admin; the substituted data is not. Only
        // the data is escaped, or every template would render as visible angle brackets.
        var result = MessageTemplateRenderer.RenderHtml("<b>Hi {{FirstName}}</b>", Values(("FirstName", "Ada")));

        Assert.Equal("<b>Hi Ada</b>", result.Text);
    }

    [Fact]
    public void Text_rendering_does_not_escape()
    {
        // An SMS has no markup context. Escaping here would send the customer "Ada &amp; Co".
        var result = MessageTemplateRenderer.RenderText("Hi {{Company}}", Values(("Company", "Ada & Co")));

        Assert.Equal("Hi Ada & Co", result.Text);
    }

    [Fact]
    public void An_unknown_placeholder_fails_rather_than_rendering_blank()
    {
        var result = MessageTemplateRenderer.RenderText("Hi {{Nonexistent}}", Values(("FirstName", "Ada")));

        Assert.False(result.Succeeded);
        Assert.Contains("Nonexistent", result.MissingFields);
        Assert.Empty(result.Text);
    }

    [Fact]
    public void A_known_but_empty_field_renders_empty_and_succeeds()
    {
        // A lead with no company legitimately has no company. That is a data state, not a
        // template bug, and must not block the send.
        var result = MessageTemplateRenderer.RenderText("Hi {{Company}}!", Values(("Company", "")));

        Assert.True(result.Succeeded);
        Assert.Equal("Hi !", result.Text);
    }

    [Fact]
    public void A_null_field_value_renders_empty_and_succeeds()
    {
        var result = MessageTemplateRenderer.RenderText("Hi {{Company}}!", Values(("Company", null)));

        Assert.True(result.Succeeded);
        Assert.Equal("Hi !", result.Text);
    }

    [Fact]
    public void Extracts_placeholder_names_for_save_time_validation()
    {
        var names = MessageTemplateRenderer.ExtractPlaceholders("{{FirstName}} at {{Company}} — {{FirstName}}");

        Assert.Equal(2, names.Count);
        Assert.Contains("FirstName", names);
        Assert.Contains("Company", names);
    }

    [Fact]
    public void Extracting_from_a_template_with_no_placeholders_returns_nothing()
    {
        Assert.Empty(MessageTemplateRenderer.ExtractPlaceholders("Just plain text"));
        Assert.Empty(MessageTemplateRenderer.ExtractPlaceholders(null));
        Assert.Empty(MessageTemplateRenderer.ExtractPlaceholders(""));
    }

    [Fact]
    public void An_unterminated_placeholder_is_left_alone_rather_than_swallowing_the_template()
    {
        var result = MessageTemplateRenderer.RenderText("Hi {{FirstName", Values(("FirstName", "Ada")));

        Assert.True(result.Succeeded);
        Assert.Equal("Hi {{FirstName", result.Text);
    }

    [Fact]
    public void TextToHtml_escapes_and_breaks_lines()
    {
        // The input here has already had customer data substituted into it, so it must be
        // escaped on the way into an HTML part.
        var html = MessageTemplateRenderer.TextToHtml("Hi <b>Ada</b>\nBye");

        Assert.DoesNotContain("<b>", html);
        Assert.Contains("&lt;b&gt;", html);
        Assert.Contains("<br />", html);
    }

    [Fact]
    public void Rendering_a_null_or_empty_template_produces_empty_output()
    {
        Assert.True(MessageTemplateRenderer.RenderText(null, Values()).Succeeded);
        Assert.Equal(string.Empty, MessageTemplateRenderer.RenderText(null, Values()).Text);
        Assert.Equal(string.Empty, MessageTemplateRenderer.RenderHtml("", Values()).Text);
    }
}
