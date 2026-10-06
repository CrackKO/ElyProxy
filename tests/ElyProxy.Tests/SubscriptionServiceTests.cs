using System.Net;
using ElyProxy.Models;
using ElyProxy.Services;

namespace ElyProxy.Tests;

public sealed class SubscriptionServiceTests
{
    [Fact]
    public async Task FetchNodesAsync_ReturnsParsedServers()
    {
        const string node = "vless://11111111-1111-1111-1111-111111111111@example.com:443#Main";
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(node)
        });
        using var service = new SubscriptionService(client);

        SubscriptionRefreshResult result = await service.FetchNodesAsync("https://example.com/sub", new ParserService());

        Assert.Equal(SubscriptionRefreshStatus.Success, result.Status);
        Assert.Equal("Main", Assert.Single(result.Nodes).Name);
    }

    [Fact]
    public async Task FetchNodesAsync_RejectsNonemptyContentWithoutServers()
    {
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not a VLESS subscription")
        });
        using var service = new SubscriptionService(client);

        SubscriptionRefreshResult result = await service.FetchNodesAsync("https://example.com/sub", new ParserService());

        Assert.Equal(SubscriptionRefreshStatus.InvalidContent, result.Status);
        Assert.Empty(result.Nodes);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task FetchNodesAsync_ReportsTrulyEmptyResponseSeparately()
    {
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("  \r\n ")
        });
        using var service = new SubscriptionService(client);

        SubscriptionRefreshResult result = await service.FetchNodesAsync("https://example.com/sub", new ParserService());

        Assert.Equal(SubscriptionRefreshStatus.Empty, result.Status);
        Assert.Empty(result.Nodes);
    }

    [Fact]
    public async Task FetchNodesAsync_RejectsHtmlResponse()
    {
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>Sign in</body></html>", System.Text.Encoding.UTF8, "text/html")
        });
        using var service = new SubscriptionService(client);

        SubscriptionRefreshResult result = await service.FetchNodesAsync("https://example.com/sub", new ParserService());

        Assert.Equal(SubscriptionRefreshStatus.InvalidContent, result.Status);
        Assert.Contains("HTML", result.ErrorMessage);
    }

    [Fact]
    public async Task FetchNodesAsync_ReportsHttpErrorWithoutEchoingSecretUrl()
    {
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var service = new SubscriptionService(client);
        const string secretUrl = "https://example.com/sub?token=private-token";

        SubscriptionRefreshResult result = await service.FetchNodesAsync(secretUrl, new ParserService());

        Assert.Equal(SubscriptionRefreshStatus.RequestFailed, result.Status);
        Assert.Contains("503", result.ErrorMessage);
        Assert.DoesNotContain("private-token", result.ErrorMessage);
    }

    [Fact]
    public async Task FetchNodesAsync_RejectsNonHttpSchemes()
    {
        using var client = CreateClient(_ => throw new InvalidOperationException("The HTTP handler must not be called."));
        using var service = new SubscriptionService(client);

        SubscriptionRefreshResult result = await service.FetchNodesAsync("file:///private/subscription", new ParserService());

        Assert.Equal(SubscriptionRefreshStatus.RequestFailed, result.Status);
        Assert.Empty(result.Nodes);
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) =>
        new(new DelegateHandler(responseFactory));

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
