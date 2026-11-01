using System.Net;
using ElyProxy.Models;
using ElyProxy.Services;

namespace ElyProxy.Tests;

public sealed class SubscriptionRefreshCoordinatorTests
{
    [Fact]
    public async Task RefreshAllAsync_StagesSuccessAndPreservesServersForEmptyOrFailedResponses()
    {
        const string oldUuid = "00000000-0000-0000-0000-000000000001";
        using var client = new HttpClient(new RouteHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/ok" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("vless://11111111-1111-1111-1111-111111111111@example.com:443#new")
            },
            "/empty" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty)
            },
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        }));
        using var fetcher = new SubscriptionService(client);
        var coordinator = new SubscriptionRefreshCoordinator(fetcher, new ParserService());
        var original = new[]
        {
            CreateSubscription("ok", oldUuid),
            CreateSubscription("empty", oldUuid),
            CreateSubscription("failed", oldUuid)
        };
        DateTime previousSuccess = DateTime.Now.AddHours(-3);
        original[1].LastUpdated = previousSuccess;
        original[2].LastUpdated = previousSuccess;

        SubscriptionRefreshBatchResult batch = await coordinator.RefreshAllAsync(original);

        Assert.Equal(1, batch.UpdatedCount);
        Assert.Equal(2, batch.FailedCount);
        Assert.Equal("new", Assert.Single(batch.Subscriptions[0].Nodes).Name);
        Assert.Equal(oldUuid, Assert.Single(batch.Subscriptions[1].Nodes).UUID);
        Assert.Equal(oldUuid, Assert.Single(batch.Subscriptions[2].Nodes).UUID);
        Assert.Equal(previousSuccess, batch.Subscriptions[1].LastUpdated);
        Assert.Equal(previousSuccess, batch.Subscriptions[2].LastUpdated);
        Assert.NotNull(batch.Subscriptions[1].LastUpdateError);
        Assert.NotNull(batch.Subscriptions[2].LastUpdateError);
        Assert.Equal(oldUuid, Assert.Single(original[0].Nodes).UUID);
    }

    private static Subscription CreateSubscription(string path, string uuid) => new()
    {
        Name = path,
        Url = "https://example.com/" + path,
        LastUpdated = DateTime.Now.AddDays(-1),
        Nodes = [new VlessNode { UUID = uuid, Address = "old.example.com", Port = 443 }]
    };

    private sealed class RouteHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
