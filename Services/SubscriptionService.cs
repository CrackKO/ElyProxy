using System.Net.Http;

namespace ElyProxy.Services;

public class SubscriptionService : IDisposable
{
    private readonly HttpClient _httpClient;

    public SubscriptionService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ElyProxy/1.0");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/plain, */*");
    }

    public async Task<string> FetchAsync(string url)
    {
        using var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
