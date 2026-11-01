using ElyProxy.Models;
using ElyProxy.Services;
using Xunit;

namespace ElyProxy.Tests;

public class ProbePresentationTests
{
    private static ProbeSample Good(ProbeTarget t, int ms = 600) => new(t.Name, true, ms,
        t.ExactBytes > 0 ? t.ExactBytes : 100, "OK", t.Control, t.ExactBytes > 0);
    private static ServerProbeResult Full(Func<ProbeTarget, int, ProbeSample>? change = null)
    {
        var samples = Enumerable.Range(0, 3).SelectMany(round => ElyHardService.Controls.Concat(ElyHardService.Services)
            .Select(t => change?.Invoke(t, round) ?? Good(t))).Concat(new[] { Good(ElyHardService.Download), Good(ElyHardService.Download) }).ToArray();
        return new("node", true, "test", samples, 2);
    }
    [Fact]
    public void SixHundredMillisecondsIsGoodHttpsNotBadTcp()
    {
        var rating = Full().Rating;
        Assert.Equal("A", rating.Grade); Assert.Equal("Good", rating.Band);
        Assert.Equal("Рекомендуется", rating.Status);
        Assert.Contains("600 мс HTTPS", rating.Metrics);
    }
    [Fact]
    public void RestrictedWebsiteDoesNotMakeHealthyProxyRed()
    {
        var result = Full((target, _) => target.Name == "Discord web" ? Good(target) with { Success = false, Detail = "HTTP 403" } : Good(target));
        Assert.Equal(ProbeVerdict.Partial, result.Verdict); // Network algorithm unchanged.
        Assert.Equal("Good", result.Rating.Band);
        Assert.True(result.Rating.Score >= 75);
        Assert.Equal("B", result.Rating.Grade); Assert.Equal("Можно*", result.Rating.Status);
        Assert.NotEqual("Не работает", result.Rating.Status);
    }
    [Fact]
    public void SlowHttpsIsYellowNotUnreachable()
    {
        var rating = Full((t, _) => Good(t, 4000)).Rating;
        Assert.Equal("Medium", rating.Band); Assert.Equal("Медленно", rating.Status);
        Assert.InRange(rating.Score!.Value, 0, 74);
    }
    [Fact]
    public void IncompleteResultHasNoFakeScore()
    {
        var result = Full() with { Starts = 1, Failure = "core startup" };
        Assert.Null(result.Rating.Score); Assert.Equal("?", result.Rating.Grade);
        Assert.Equal("Unknown", result.Rating.Band);
    }
    [Fact]
    public void NoSuccessfulControlResponseIsRed()
    {
        var samples = ElyHardService.Controls.Select(t => Good(t) with { Success = false, Detail = "TCP: timeout" }).ToArray();
        var rating = new ServerProbeResult("node", true, "test", samples, 1, "control failed").Rating;
        Assert.Equal("F", rating.Grade); Assert.Equal("High", rating.Band);
    }
    [Fact]
    public void ControlHttpRestrictionIsNotProofOfDeadProxy()
    {
        var samples = ElyHardService.Controls.Select(t => Good(t) with { Success = false, Detail = "HTTP 403" }).ToArray();
        var rating = new ServerProbeResult("node", true, "test", samples, 1, "control failed").Rating;
        Assert.Null(rating.Score); Assert.Equal("Unknown", rating.Band);
    }
    [Fact]
    public void VariabilityIsMeasuredWithinSameTargetNotAcrossDifferentSites()
    {
        var stable = Full((t, _) => Good(t, t.Name == "Discord web" ? 2200 : 400)).Rating;
        Assert.NotEqual("С перебоями", stable.Status);
        var unstable = Full((t, round) => Good(t, t.Name == "Discord web" && round == 2 ? 2200 : 400)).Rating;
        Assert.Equal("С перебоями", unstable.Status); Assert.Equal("Medium", unstable.Band);
    }
    [Fact]
    public void FailedTransferIsCautionAndLowersRank()
    {
        var healthy = Full();
        var samples = healthy.Samples.ToArray(); samples[^1] = samples[^1] with { Success = false };
        var rating = (healthy with { Samples = samples }).Rating;
        Assert.Equal("Medium", rating.Band); Assert.Equal("С перебоями", rating.Status);
        Assert.True(rating.Score < healthy.Rating.Score);
    }
    [Fact]
    public void TurboIsProvisionalAndCannotReceiveAGrade()
    {
        var samples = ElyHardService.Controls.Concat(ElyHardService.Services.Take(3)).Select(t => Good(t)).ToArray();
        var rating = new ServerProbeResult("node", false, "test", samples, 1).Rating;
        Assert.Equal("B", rating.Grade); Assert.InRange(rating.Score!.Value, 75, 85);
        Assert.DoesNotContain("Мбит", rating.Metrics);
    }
    [Theory]
    [InlineData(800, "Good")]
    [InlineData(801, "Medium")]
    [InlineData(5000, "Medium")]
    public void HttpsResponseTimeAloneNeverTurnsRed(int ms, string band) =>
        Assert.Equal(band, ProbeRatingPolicy.HttpsLatencyBand(ms));

    [Theory]
    [InlineData(16, 8, 16, 16)]
    [InlineData(16, 1, 16, 2)]
    [InlineData(16, 8, 2, 2)]
    [InlineData(0, 8, 16, 1)]
    [InlineData(32, 64, 64, 16)]
    [InlineData(10, 8, 0, 1)]
    public void ParallelismUsesRequestedCapacityCpuAndMemory(int requested, int cpu, int freeGiB, int expected) =>
        Assert.Equal(expected, ProbeExecutionPolicy.Resolve(requested, cpu, (ulong)freeGiB * 1024 * 1024 * 1024));

    private sealed class Lease(Action stop) : IAsyncDisposable
    { public ValueTask DisposeAsync() { stop(); return ValueTask.CompletedTask; } }
    private static readonly ElyProxy.Core.XrayTunOptions Options = new("Probe", 1500, [], false, [], []);
    [Fact]
    public async Task ParallelNodesKeepAllTwentySixChecksAndSerializeBulkTransfers()
    {
        int transfers = 0, peak = 0, starts = 0, stops = 0;
        var service = new ElyHardService((_, _) =>
        {
            Interlocked.Increment(ref starts);
            return Task.FromResult<IAsyncDisposable>(new Lease(() => Interlocked.Increment(ref stops)));
        }, async (_, target, _, token) =>
        {
            if (target.ExactBytes > 0)
            {
                int active = Interlocked.Increment(ref transfers);
                peak = Math.Max(peak, active);
                try { await Task.Delay(10, token); }
                finally { Interlocked.Decrement(ref transfers); }
            }
            return Good(target);
        }, (_, _) => Task.CompletedTask);
        using var gate = new SemaphoreSlim(1, 1);
        var context = new ProbeContext(new VlessNode(), Options, ElyProxy.Core.ProxyCoreKind.SingBox, "test");
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => service.CheckNodeAsync(context, true, default, gate)));
        Assert.Equal(1, peak); Assert.Equal(8, starts); Assert.Equal(8, stops);
        Assert.All(results, r => { Assert.Equal(26, r.Total); Assert.Equal(ProbeVerdict.Verified, r.Verdict); });
    }
    [Fact]
    public async Task CancelWhileWaitingForTransferSlotReleasesCoreWithoutStealingSlot()
    {
        int stops = 0;
        var service = new ElyHardService((_, _) => Task.FromResult<IAsyncDisposable>(new Lease(() => stops++)),
            (_, t, _, _) => Task.FromResult(Good(t)), (_, _) => Task.CompletedTask);
        using var gate = new SemaphoreSlim(0, 1);
        using var cancel = new CancellationTokenSource();
        var context = new ProbeContext(new VlessNode(), Options, ElyProxy.Core.ProxyCoreKind.SingBox, "test");
        var task = service.CheckNodeAsync(context, true, cancel.Token, gate);
        Assert.False(task.IsCompleted); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(1, stops); Assert.Equal(0, gate.CurrentCount);
    }
}
