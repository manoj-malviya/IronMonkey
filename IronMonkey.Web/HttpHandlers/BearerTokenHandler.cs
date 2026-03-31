using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace IronMonkey.Web.HttpHandlers;

public class BearerTokenHandler : DelegatingHandler
{
    private readonly ProtectedSessionStorage _sessionStorage;
    private readonly ILogger<BearerTokenHandler> _logger;

    public BearerTokenHandler(
        ProtectedSessionStorage sessionStorage,
        ILogger<BearerTokenHandler> logger)
    {
        _sessionStorage = sessionStorage;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var tokenResult = await _sessionStorage.GetAsync<string>("auth_token");

            if (tokenResult.Success && !string.IsNullOrEmpty(tokenResult.Value))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", tokenResult.Value);
            }
            else
            {
                _logger.LogWarning("No auth token in session storage for request to {Uri}", request.RequestUri);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error attaching bearer token to request");
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("API returned 401 Unauthorized for {Uri} — session may be expired", request.RequestUri);
        }

        return response;
    }
}
