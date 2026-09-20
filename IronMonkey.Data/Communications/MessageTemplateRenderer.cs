using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace IronMonkey.Data.Communications;

/// <summary>Outcome of rendering a template against one record's values.</summary>
/// <param name="Text">The rendered output. Empty when <paramref name="MissingFields"/> is non-empty.</param>
/// <param name="MissingFields">Placeholders that named something the record has no value for.</param>
public readonly record struct TemplateRenderResult(string Text, IReadOnlyList<string> MissingFields)
{
    public bool Succeeded => MissingFields.Count == 0;
}

/// <summary>
/// Renders <c>{{Placeholder}}</c> templates against a record's field values.
///
/// <para><b>Every substituted value is escaped.</b> A lead's name, company and custom field
/// values are attacker-controlled: a web form is a public endpoint, so anyone can create a
/// lead called <c>&lt;script&gt;…&lt;/script&gt;</c>. That value is rendered into an HTML
/// email and, on the timeline, back into the tenant's own browser. Escaping at substitution
/// time — rather than trusting the template author or the reader — is what keeps a hostile
/// lead name from becoming script execution in either place.</para>
///
/// <para>Escaping is chosen by the output format, not by the channel, because the same
/// template text is rendered as HTML for email and as plain text for SMS. Escaping plain
/// text would put <c>&amp;amp;</c> in an SMS; not escaping HTML would be an injection.</para>
///
/// <para>Placeholders resolve against a supplied dictionary rather than a <c>Lead</c>, so
/// custom fields (keyed by definition id) and contacts work through the same path — and so
/// this type stays testable without a database.</para>
/// </summary>
public static class MessageTemplateRenderer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Matches <c>{{ Name }}</c>. The inner pattern excludes braces so an unterminated
    /// placeholder cannot swallow the rest of the template.
    /// </summary>
    private static readonly Regex Placeholder = new(
        @"\{\{\s*(?<name>[^{}\s][^{}]*?)\s*\}\}",
        RegexOptions.CultureInvariant, RegexTimeout);

    /// <summary>
    /// Renders for HTML output (email bodies). Substituted values are HTML-escaped; the
    /// template's own markup is preserved, since the template author is a trusted tenant
    /// Admin while the substituted data is not.
    /// </summary>
    public static TemplateRenderResult RenderHtml(string? template, IReadOnlyDictionary<string, string?> values)
        => Render(template, values, WebUtility.HtmlEncode);

    /// <summary>
    /// Renders for plain-text output (SMS, WhatsApp, and the text part of an email).
    /// Values are inserted verbatim — there is no markup context to escape into, and
    /// escaping here would corrupt ordinary characters like &amp; in a customer's name.
    /// </summary>
    public static TemplateRenderResult RenderText(string? template, IReadOnlyDictionary<string, string?> values)
        => Render(template, values, value => value);

    /// <summary>
    /// The placeholder names a template uses, for validating it against real field
    /// definitions at save time.
    /// </summary>
    public static IReadOnlyList<string> ExtractPlaceholders(string? template)
    {
        if (string.IsNullOrEmpty(template) || !template.Contains("{{", StringComparison.Ordinal))
            return [];

        var names = new List<string>();

        try
        {
            foreach (Match match in Placeholder.Matches(template))
            {
                var name = match.Groups["name"].Value;
                if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    names.Add(name);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return [];
        }

        return names;
    }

    private static TemplateRenderResult Render(
        string? template,
        IReadOnlyDictionary<string, string?> values,
        Func<string, string> escape)
    {
        if (string.IsNullOrEmpty(template)) return new TemplateRenderResult(string.Empty, []);
        if (!template.Contains("{{", StringComparison.Ordinal))
            return new TemplateRenderResult(template, []);

        var missing = new List<string>();
        string rendered;

        try
        {
            rendered = Placeholder.Replace(template, match =>
            {
                var name = match.Groups["name"].Value;

                if (!values.TryGetValue(name, out var value))
                {
                    // An unknown placeholder is a template bug, not a blank field. Recording
                    // it rather than silently emitting "" is what lets the caller refuse to
                    // send a half-rendered message — and lets save-time validation point at
                    // the exact placeholder.
                    if (!missing.Contains(name, StringComparer.OrdinalIgnoreCase))
                        missing.Add(name);
                    return match.Value;
                }

                // A known field that is simply empty renders as empty. That is a data state,
                // not a template error: a lead with no company legitimately has no company.
                return escape(value ?? string.Empty);
            });
        }
        catch (RegexMatchTimeoutException)
        {
            return new TemplateRenderResult(string.Empty, ["<template too complex to render>"]);
        }

        return new TemplateRenderResult(missing.Count == 0 ? rendered : string.Empty, missing);
    }

    /// <summary>
    /// Converts a plain-text body into minimal HTML for an email part: escapes the text and
    /// turns newlines into breaks. Used when a tenant writes a plain-text template but the
    /// channel needs HTML — it must escape, because that text contains already-substituted
    /// customer data.
    /// </summary>
    public static string TextToHtml(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var encoded = WebUtility.HtmlEncode(text);
        var builder = new StringBuilder(encoded.Length + 16);
        builder.Append(encoded.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "<br />"));
        return builder.ToString();
    }
}
