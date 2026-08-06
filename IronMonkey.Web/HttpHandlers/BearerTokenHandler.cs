using System.Net.Http.Headers;
using IronMonkey.Web.Authentication;

namespace IronMonkey.Web.HttpHandlers;

public class BearerTokenHandler : DelegatingHandler
{
    private readonly AdminAuthenticationStateProvider _authStateProvider;
    private readonly ILogger<BearerTokenHandler> _logger;

    public BearerTokenHandler(
        AdminAuthenticationStateProvider authStateProvider,
        ILogger<BearerTokenHandler> logger)
    {
        _authStateProvider = authStateProvider;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Read through the auth provider rather than session storage directly. It serves
        // an in-memory copy of the token, so no JS interop happens here — the previous
        // direct ProtectedSessionStorage call threw during prerendering and the request
        // then went out with no Authorization header at all.
        var token = await _authStateProvider.GetTokenAsync();

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            _logger.LogDebug("No auth token available for request to {Uri}", request.RequestUri);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("API returned 401 Unauthorized for {Uri} — session may be expired", request.RequestUri);
        }

        return response;
    }
}
