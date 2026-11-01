using System.Net;
using System.Text;
using ElyProxy.Core;
using ElyProxy.Models;
using ElyProxy.Services;
using Xunit;

namespace ElyProxy.Tests;

public class ServerProbeTests
{
    private static ProbeContext Context => new(new VlessNode(), new XrayTunOptions("Probe", 1500, [], false, [], []), ProxyCoreKind.SingBox, "test");
    private sealed class Lease(Action dispose) : IAsyncDisposable
    { public ValueTask DisposeAsync() { dispose(); return ValueTask.CompletedTask; } }
    private sealed class Reply(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
    private static ProbeSample Good(ProbeTarget t, int ms = 100) => new(t.Name, true, ms, t.ExactBytes > 0 ? t.ExactBytes : 100, "OK", t.Control, t.ExactBytes > 0);
    private static ElyHardService Fake(Func<ProbeTarget, ProbeSample> sample, Action? started = null, Action? disposed = null) =>
        new((_, _) => { started?.Invoke(); return Task.FromResult<IAsyncDisposable>(new Lease(() => disposed?.Invoke())); },
            (_, t, _, token) => { token.ThrowIfCancellationRequested(); return Task.FromResult(sample(t)); }, (_, _) => Task.CompletedTask);

    [Fact]
    public async Task HardRequiresTwoIndependentCoreSessionsThreeRoundsAndFullTransfers()
    {
        int starts = 0, stops = 0;
        var result = await Fake(t => Good(t), () => starts++, () => stops++).CheckNodeAsync(Context, true, default);
        Assert.Equal(2, starts); Assert.Equal(2, stops); Assert.Equal(26, result.Total);
        Assert.Equal(6, result.Samples.Count(s => s.Control));
        Assert.Equal(2, result.Samples.Count(s => s.Transfer));
        Assert.Equal(ProbeVerdict.Verified, result.Verdict);
        Assert.Contains("UDP", result.Report); Assert.Contains("TUN", result.Report);
    }

    [Fact]
    public void PartialCoverageMustNeverBecomeVerified()
    {
        var samples = ElyHardService.Controls.Select(t => Good(t))
            .Concat(new[] { Good(ElyHardService.Download), Good(ElyHardService.Download) }).ToArray();
        var result = new ServerProbeResult("node", true, "test", samples, 2);
        Assert.Equal(ProbeVerdict.Inconclusive, result.Verdict);
    }

    [Fact]
    public async Task TurboIsSingleSessionWithoutDownloadAndNeverClaimsHardVerification()
    {
        var result = await Fake(t => Good(t)).CheckNodeAsync(Context, false, default);
        Assert.Equal(1, result.Starts); Assert.Equal(5, result.Total);
        Assert.DoesNotContain(result.Samples, s => s.Transfer);
        Assert.Equal(ProbeVerdict.Quick, result.Verdict);
    }

    [Theory]
    [InlineData("download", ProbeVerdict.Unstable)]
    [InlineData("service", ProbeVerdict.Partial)]
    [InlineData("slow", ProbeVerdict.Slow)]
    [InlineData("tail", ProbeVerdict.Slow)]
    public async Task FailedOrSlowWorkloadCannotBeVerified(string fault, ProbeVerdict expected)
    {
        var result = await Fake(t => fault switch
        {
            "download" when t.ExactBytes > 0 => Good(t) with { Success = false },
            "service" when t.Name == "Discord web" => Good(t) with { Success = false, Detail = "HTTP 403" },
            "slow" when t.ExactBytes > 0 => Good(t, 12000),
            "tail" when t.Name == "Telegram web" => Good(t, 4000),
            _ => Good(t)
        }).CheckNodeAsync(Context, true, default);
        Assert.Equal(expected, result.Verdict);
    }

    [Fact]
    public async Task MissingControlsDoNotPretendOtherSitesWereTested()
    {
        var result = await Fake(t => Good(t) with { Success = false }).CheckNodeAsync(Context, true, default);
        Assert.Equal(2, result.Total); Assert.All(result.Samples, s => Assert.True(s.Control));
        Assert.Equal(ProbeVerdict.Inconclusive, result.Verdict);
    }

    [Fact]
    public async Task CoreStartupFailureIsNotADeadServer()
    {
        var service = new ElyHardService((_, _) => throw new IOException("missing core"),
            (_, t, _, _) => Task.FromResult(Good(t)), (_, _) => Task.CompletedTask);
        var result = await service.CheckNodeAsync(Context, true, default);
        Assert.Equal(ProbeVerdict.Inconclusive, result.Verdict); Assert.Contains("missing core", result.Failure);
    }

    [Fact]
    public async Task CancellationWaitsForLeaseDisposalAndDoesNotBecomeNetworkFailure()
    {
        int disposed = 0;
        using var cancel = new CancellationTokenSource();
        var service = new ElyHardService((_, _) => Task.FromResult<IAsyncDisposable>(new Lease(() => disposed++)),
            (_, _, _, token) => { cancel.Cancel(); token.ThrowIfCancellationRequested(); throw new Exception(); },
            (_, _) => Task.CompletedTask);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CheckNodeAsync(Context, true, cancel.Token));
        Assert.Equal(1, disposed);
    }

    [Fact]
    public async Task CleanupFailureAbortsBatchInsteadOfStartingAnotherCore()
    {
        var service = new ElyHardService((_, _) => Task.FromResult<IAsyncDisposable>(new Lease(() => throw new IOException("stop failed"))),
            (_, t, _, _) => Task.FromResult(Good(t)), (_, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<ProbeCleanupException>(() => service.CheckNodeAsync(Context, true, default));
    }

    [Theory]
    [InlineData(200, "telegram", true)]
    [InlineData(200, "login firewall", false)]
    [InlineData(200, "", false)]
    [InlineData(302, "telegram", false)]
    [InlineData(403, "telegram", false)]
    [InlineData(429, "telegram", false)]
    [InlineData(451, "telegram", false)]
    [InlineData(500, "telegram", false)]
    public async Task HttpResponseMustHaveExpectedStatusAndContent(int code, string body, bool expected)
    {
        using var client = new HttpClient(new Reply((request, _) =>
        {
            Assert.True(request.Headers.ConnectionClose);
            Assert.True(request.Headers.CacheControl!.NoCache);
            Assert.Equal(HttpVersion.Version11, request.Version);
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(body) });
        }));
        var sample = await ProbeHttpTransport.MeasureWithClientAsync(client,
            new("Telegram", "https://telegram.org/", "telegram"), TimeSpan.FromSeconds(1), default);
        Assert.Equal(expected, sample.Success);
    }

    [Theory]
    [InlineData(16, 16, true)]
    [InlineData(8, 16, false)]
    [InlineData(24, 16, false)]
    public async Task TransferMustFinishWithExactRequestedByteCount(int actual, int requested, bool expected)
    {
        using var client = new HttpClient(new Reply((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new ByteArrayContent(new byte[actual]) })));
        var sample = await ProbeHttpTransport.MeasureWithClientAsync(client,
            new("Download", "https://test.example/", ExactBytes: requested), TimeSpan.FromSeconds(1), default);
        Assert.Equal(expected, sample.Success);
    }

    [Fact]
    public async Task HtmlErrorPageIsNotBandwidthMeasurement()
    {
        using var client = new HttpClient(new Reply((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("blocked", Encoding.UTF8, "text/html") })));
        var sample = await ProbeHttpTransport.MeasureWithClientAsync(client,
            new("Download", "https://test.example/", ExactBytes: 7), TimeSpan.FromSeconds(1), default);
        Assert.False(sample.Success);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("unexpected", false)]
    public async Task Empty204ContractIsChecked(string body, bool expected)
    {
        using var client = new HttpClient(new Reply((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)
        { Content = new StringContent(body) })));
        var sample = await ProbeHttpTransport.MeasureWithClientAsync(client,
            new("204", "https://test.example/", "204"), TimeSpan.FromSeconds(1), default);
        Assert.Equal(expected, sample.Success);
    }

    [Fact]
    public async Task RequestDeadlineAndUserCancellationAreDifferentResults()
    {
        using var client = new HttpClient(new Reply(async (_, token) =>
        { await Task.Delay(Timeout.InfiniteTimeSpan, token); throw new Exception(); }));
        var target = new ProbeTarget("test", "https://test.example/", "test");
        var timeout = await ProbeHttpTransport.MeasureWithClientAsync(client, target, TimeSpan.FromMilliseconds(30), default);
        Assert.False(timeout.Success); Assert.Contains("Тайм-аут", timeout.Detail);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ProbeHttpTransport.MeasureWithClientAsync(client, target, TimeSpan.FromSeconds(1), cancel.Token));
    }
}
