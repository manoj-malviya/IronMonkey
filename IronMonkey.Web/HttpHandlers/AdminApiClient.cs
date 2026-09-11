using System.Net.Http.Headers;
using IronMonkey.Web.Authentication;

namespace IronMonkey.Web.HttpHandlers;

/// <summary>
/// Calls the API with the current user's JWT attached.
///
/// Pages must not attach the token via BearerTokenHandler alone: message handlers created
/// by IHttpClientFactory live in their own DI scope, separate from the Blazor circuit's, so
/// the handler resolves a different AdminAuthenticationStateProvider than the page — one
/// that never observed the login and holds no token. Requests then go out with no
/// Authorization header and come back 401, leaving admin pages permanently empty.
///
/// This type is scoped, so it shares the circuit's provider instance and reads the token
/// that LoginAsync actually cached.
/// </summary>
public class AdminApiClient(
    IHttpClientFactory httpClientFactory,
    AdminAuthenticationStateProvider authStateProvider)
{
    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string uri,
        HttpContent? content = null,
        CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("AdminApi");
        var request = new HttpRequestMessage(method, uri);

        if (content is not null)
            request.Content = content;

        // Pages fetch from OnInitializedAsync, which also runs during prerender where JS
        // interop — and therefore ProtectedSessionStorage — is unavailable. Ask the provider
        // to initialize first: once interop is up this loads the token, and during prerender
        // it is a no-op, so the request is simply sent unauthenticated and retried on the
        // interactive render. Without this every page's first fetch 401s.
        await authStateProvider.InitializeAsync();

        var token = await authStateProvider.GetTokenAsync();
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.SendAsync(request, cancellationToken);
    }

    public Task<HttpResponseMessage> GetAsync(string uri, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Get, uri, content: null, cancellationToken);

    public Task<HttpResponseMessage> PostAsync(
        string uri, HttpContent? content = null, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, uri, content, cancellationToken);

    public Task<HttpResponseMessage> PostAsJsonAsync<T>(
        string uri, T value, CancellationToken cancellationToken = default)
        => SendAsync(
            HttpMethod.Post,
            uri,
            JsonContent.Create(value),
            cancellationToken);

    public Task<HttpResponseMessage> PutAsJsonAsync<T>(
        string uri, T value, CancellationToken cancellationToken = default)
        => SendAsync(
            HttpMethod.Put,
            uri,
            JsonContent.Create(value),
            cancellationToken);

    public Task<HttpResponseMessage> DeleteAsync(string uri, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Delete, uri, content: null, cancellationToken);

    /// <summary>
    /// GETs and deserializes, returning default on a non-success status rather than throwing —
    /// callers render an empty/error state instead of faulting the circuit.
    /// </summary>
    public async Task<T?> GetFromJsonAsync<T>(string uri, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync(uri, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return default;

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }
}
