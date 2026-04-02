namespace IronMonkey.Web;

public class ApiClient(HttpClient httpClient)
{
    public async Task<T?> GetAsync<T>(string url)
    {
        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>();
    }

    public async Task<HttpResponseMessage> PostAsync<T>(string url, T body)
    {
        return await httpClient.PostAsJsonAsync(url, body);
    }
}
