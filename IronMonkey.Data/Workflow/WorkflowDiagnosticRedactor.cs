using System.Text.RegularExpressions;

namespace IronMonkey.Data.Workflow;

/// <summary>
/// Sanitizes workflow diagnostics before they are persisted or returned to a tenant Admin.
///
/// Every string that reaches <c>WorkflowExecutionLog.ErrorMessage</c> or
/// <c>WorkflowExecutionStep.Message</c> passes through here. The reason this is one type
/// rather than care taken at each call site: the engine writes diagnostics from exception
/// messages, and an exception from an HTTP client or a JSON parser routinely quotes the
/// input that caused it — a webhook URL with a signed token in the query string, or the
/// action JSON including an <c>X-Api-Key</c> header value. Each of those is a credential
/// leak into a table a tenant Admin can read and export.
///
/// It lives in IronMonkey.Data rather than the API service because the entities it protects
/// do, so nothing can reference the entity without the redactor being available.
/// </summary>
public static class WorkflowDiagnosticRedactor
{
    /// <summary>
    /// Cap on a persisted diagnostic. A long exception message is not more diagnostic than
    /// its first 500 characters, and an unbounded column invites a stack trace — which is an
    /// operator artifact that belongs in ILogger, not in tenant-visible history.
    /// </summary>
    public const int MaxMessageLength = 500;

    private const string Placeholder = "[redacted]";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Header and property names whose values are never stored. Matched case-insensitively
    /// against <c>name: value</c>, <c>name=value</c> and <c>"name":"value"</c> shapes, which
    /// is how they appear in both exception text and raw action JSON.
    /// </summary>
    private static readonly string[] SensitiveNames =
    [
        "authorization", "auth", "x-api-key", "apikey", "api_key", "api-key",
        "password", "passwd", "pwd", "secret", "client_secret", "clientsecret",
        "token", "access_token", "refresh_token", "id_token", "bearer",
        "signature", "sig", "x-hub-signature", "x-signature", "cookie", "set-cookie",
        "private_key", "privatekey", "credential", "credentials", "session"
    ];

    private static readonly Regex SensitiveAssignment = new(
        // name, then : or =, optionally quoted, then the value up to the next delimiter.
        @"(?<name>" + string.Join("|", SensitiveNames.Select(Regex.Escape)) + @")(?<sep>""?\s*[:=]\s*""?)(?<value>[^""',;&\r\n}\]]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    /// <summary>A bare bearer/basic credential with no name in front of it.</summary>
    private static readonly Regex BareAuthScheme = new(
        @"\b(?<scheme>Bearer|Basic)\s+(?<value>[A-Za-z0-9\-._~+/=]{8,})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    /// <summary>
    /// Any absolute http(s) URL, so its query string and userinfo can be dropped. A URL is
    /// the single most common carrier of a secret in this codepath: a webhook with
    /// <c>?token=…</c> is the documented way most services authenticate an inbound hook.
    /// </summary>
    private static readonly Regex AbsoluteUrl = new(
        @"https?://[^\s""'<>\\\]}),]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    /// <summary>
    /// Redacts and truncates a diagnostic for persistence. Returns null for null/blank input
    /// so an absent message stays absent rather than becoming an empty string the UI renders
    /// as a blank row.
    /// </summary>
    public static string? Redact(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;

        var result = message;

        try
        {
            // URLs first: the later rules would otherwise match inside a query string and
            // leave the rest of it intact.
            // SafeUrl only returns null for blank input, which a regex match never is.
            result = AbsoluteUrl.Replace(result, m => SafeUrl(m.Value)!);
            result = SensitiveAssignment.Replace(result, m => m.Groups["name"].Value + m.Groups["sep"].Value + Placeholder);
            result = BareAuthScheme.Replace(result, m => m.Groups["scheme"].Value + " " + Placeholder);
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological input must not produce an unredacted message. Dropping the
            // detail loses diagnostics; keeping it could leak a credential.
            return "Diagnostic omitted: message could not be safely redacted.";
        }

        // Collapse newlines — a multi-line exception message renders as one unreadable cell
        // in the UI, and the extra lines are usually a stack trace.
        result = result.Replace("\r", " ").Replace("\n", " ").Trim();

        return Truncate(result);
    }

    /// <summary>
    /// Reduces a URL to scheme, host, port and path. The query string and any
    /// <c>user:pass@</c> userinfo are dropped entirely rather than masked — neither is
    /// needed to identify which endpoint a rule called, and both routinely carry secrets.
    /// </summary>
    public static string? SafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        // Trim trailing punctuation the regex may have swept up from surrounding prose.
        var trimmed = url.TrimEnd('.', ',', ';', ':', ')', ']', '}', '"', '\'');

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return Placeholder;

        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        var path = uri.AbsolutePath == "/" ? string.Empty : uri.AbsolutePath;
        var suffix = string.IsNullOrEmpty(uri.Query) ? string.Empty : "?" + Placeholder;

        return $"{uri.Scheme}://{uri.Host}{port}{path}{suffix}";
    }

    /// <summary>
    /// Scheme, host and port only — used for the step's TargetHost column, where the path is
    /// already in the message and the column exists to group runs by destination.
    /// </summary>
    public static string? SafeHost(Uri? uri)
    {
        if (uri is null) return null;
        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        return $"{uri.Scheme}://{uri.Host}{port}";
    }

    /// <summary>
    /// Masks an email to its shape: <c>jane.doe@example.com</c> becomes <c>j***@example.com</c>.
    ///
    /// The domain survives because a rule mailing the wrong domain is the failure an Admin
    /// needs to see, while the local part is the personal data. A value that is not an email
    /// at all is masked wholesale rather than echoed — a misconfigured rule can put anything
    /// in "to", including a token.
    /// </summary>
    public static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        var trimmed = email.Trim();
        var at = trimmed.LastIndexOf('@');

        if (at <= 0 || at == trimmed.Length - 1)
            return "***";

        var local = trimmed[..at];
        var domain = trimmed[(at + 1)..];

        var visible = local.Length == 1 ? local : local[..1];
        return $"{visible}***@{domain}";
    }

    private static string Truncate(string value) =>
        value.Length <= MaxMessageLength ? value : value[..MaxMessageLength] + "…";
}
