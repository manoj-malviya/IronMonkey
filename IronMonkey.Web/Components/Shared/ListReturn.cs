namespace IronMonkey.Web.Components.Shared;

/// <summary>
/// Resolves the "back to the list" target for a detail page.
///
/// List pages hand their full filter/sort/page state to a detail page as a returnUrl, so
/// returning lands on the same view the user left rather than a reset list. Only
/// same-origin relative paths are honoured: the value arrives from the query string, so
/// echoing it into a navigation unchecked would let a crafted link bounce a signed-in user
/// to an external site from inside the app.
/// </summary>
public static class ListReturn
{
    /// <summary>
    /// Returns <paramref name="returnUrl"/> when it is a safe in-app path, otherwise
    /// <paramref name="fallback"/>.
    /// </summary>
    public static string Resolve(string? returnUrl, string fallback)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return fallback;

        // Must be rooted at "/" and must not begin a protocol-relative URL ("//host"),
        // which a browser treats as absolute and off-site.
        if (!returnUrl.StartsWith('/') || returnUrl.StartsWith("//"))
            return fallback;

        // "/\evil.com" is normalized to a protocol-relative URL by some browsers.
        if (returnUrl.StartsWith("/\\"))
            return fallback;

        return returnUrl;
    }
}
