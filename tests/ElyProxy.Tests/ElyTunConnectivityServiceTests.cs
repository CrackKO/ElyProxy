using System.Net;
using ElyProxy.Services;
using Xunit;

namespace ElyProxy.Tests;

public sealed class ElyTunConnectivityServiceTests
{
    [Fact]
    public async Task IndependentEndpointCanConfirmConnectivityWhenGoogleFails()
    {
        using var http = new HttpClient(new Handler((request, _) =>
            request.RequestUri!.Host == "www.gstatic.com"
                ? throw new HttpRequestException("Google unavailable")
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        var result = await new ElyTunConnectivityService(http).CheckAsync();
        Assert.True(result.Reachable);
        Assert.Contains("cloudflare.com", result.Details);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.ProxyAuthenticationRequired, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task HttpsResponseDistinguishesReachabilityFromEndpointErrors(HttpStatusCode status, bool expected)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status))));
        var result = await new ElyTunConnectivityService(http).CheckAsync();
        Assert.Equal(expected, result.Reachable);
        Assert.Contains(((int)status).ToString(), result.Details);
    }

    [Fact]
    public async Task FailuresKeepBothEndpointReasonsWithoutClaimingNetworkIsOffline()
    {
        using var http = new HttpClient(new Handler((_, _) => throw new HttpRequestException("Unavailable")));
        var result = await new ElyTunConnectivityService(http).CheckAsync();
        Assert.False(result.Reachable);
        Assert.Contains("gstatic.com", result.Details);
        Assert.Contains("cloudflare.com", result.Details);
    }

    [Fact]
    public async Task ExternalCancellationIsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return new(HttpStatusCode.OK);
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ElyTunConnectivityService(http).CheckAsync(cancellation.Token));
    }

    [Fact]
    public async Task SuccessCancelsOutstandingProbe()
    {
        bool canceled = false;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            if (request.RequestUri!.Host == "www.cloudflare.com") return new(HttpStatusCode.OK);
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { canceled = true; throw; }
            return new(HttpStatusCode.OK);
        }));
        var result = await new ElyTunConnectivityService(http).CheckAsync();
        Assert.True(result.Reachable);
        Assert.True(canceled);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
