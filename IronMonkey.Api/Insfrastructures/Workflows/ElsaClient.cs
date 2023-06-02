namespace IronMonkey.Api.Infrastructures.Workflows;

public class ElsaClient
{
    private readonly HttpClient _httpClient;

    public ElsaClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task ReportTaskCompletedAsync(string taskId, object? result = default, CancellationToken cancellationToken = default)
    {
        var url = new Uri($"tasks/{taskId}/complete", UriKind.Relative);
        var request = new { Result = result };
        await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
    }
}