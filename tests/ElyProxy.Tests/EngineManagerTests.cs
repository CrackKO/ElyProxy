using ElyProxy.Core;
using ElyProxy.Models;
using ElyProxy.Services;
using Xunit;
namespace ElyProxy.Tests;
public class EngineManagerTests
{
    private static CoreCandidate[] Candidates => new[] { new CoreCandidate(ProxyCoreKind.Xray, 60, null), new CoreCandidate(ProxyCoreKind.SingBox, 50, null) };
    [Fact] public async Task AutoFallsBackAndDoesNotRepeat()
    {
        using var attempt = new ConnectionAttempt("hash", "Proxy", Candidates.Concat(Candidates).ToArray());
        var started = new List<ProxyCoreKind>();
        await attempt.RunAsync(true, (c,t) => { started.Add(c.Core); if (c.Core == ProxyCoreKind.Xray) throw new InvalidOperationException(); return Task.CompletedTask; }, () => Task.CompletedTask, (c,s,d,f) => {});
        Assert.Equal(new[] { ProxyCoreKind.Xray, ProxyCoreKind.SingBox }, started);
    }
    [Fact] public async Task ManualDoesNotFallback()
    {
        using var attempt = new ConnectionAttempt("hash", "Proxy", Candidates);
        await Assert.ThrowsAsync<InvalidOperationException>(() => attempt.RunAsync(false, (c,t) => throw new InvalidOperationException(), () => Task.CompletedTask, (c,s,d,f) => {}));
        Assert.Single(attempt.AttemptedCores);
    }
    [Fact] public async Task NewConnectOrDisconnectCancelsOldFallback()
    {
        using var old = new ConnectionAttempt("A", "Proxy", Candidates);
        int records = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => old.RunAsync(true, (c,t) => { old.Cancel(); throw new InvalidOperationException(); }, () => Task.CompletedTask, (c,s,d,f) => records++));
        Assert.Single(old.AttemptedCores); Assert.Equal(0, records);
    }
    [Fact] public async Task ExhaustionTriesEachCoreOnce()
    {
        using var attempt = new ConnectionAttempt("hash", "Proxy", Candidates.Concat(Candidates).ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(() => attempt.RunAsync(true, (c,t) => throw new InvalidOperationException(), () => Task.CompletedTask, (c,s,d,f) => {}));
        Assert.Equal(2, attempt.AttemptedCores.Count);
    }
    [Fact] public async Task FirstSuccessStopsChain()
    {
        using var attempt = new ConnectionAttempt("hash", "Proxy", Candidates);
        await attempt.RunAsync(true, (c,t) => Task.CompletedTask, () => Task.CompletedTask, (c,s,d,f) => {});
        Assert.Single(attempt.AttemptedCores);
    }
    [Fact] public void HistoryRoundtripAndSecrets()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
        try {
            string path = Path.Combine(dir,"history.json"); var history = new CorePerformanceHistoryService(path);
            var node = new VlessNode { Password = "secret-password", UUID = "secret-uuid" };
            string key = CorePerformanceHistoryService.Fingerprint(node,"Proxy","Xray");
            Assert.Equal(key, CorePerformanceHistoryService.Fingerprint(node,"Proxy","Xray"));
            history.Record(key,false,10,"CoreStartupFailure"); history.Record(key,true,200);
            var record = new CorePerformanceHistoryService(path).Get(key)!;
            Assert.Equal(1,record.Successes); Assert.Equal(1,record.Failures); Assert.Equal(0,record.ConsecutiveFailures);
            Assert.DoesNotContain("secret",File.ReadAllText(path));
            File.WriteAllText(path,"broken"); Assert.Null(new CorePerformanceHistoryService(path).Get(key));
        } finally { Directory.Delete(dir,true); }
    }
    [Fact] public void HistoryChangesRankingAndCannotEnableUnsupportedCore()
    {
        string path = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        try {
            var history = new CorePerformanceHistoryService(path); var node = new VlessNode();
            for(int i=0;i<8;i++) history.Record(CorePerformanceHistoryService.Fingerprint(node,"Proxy","Xray"),true,100);
            Assert.Equal(ProxyCoreKind.Xray,CoreCandidateSelector.Evaluate(node,ProxyCoreKind.Auto,true,true,history:history)[0].Core);
            node.Protocol="tuic";
            Assert.False(CoreCandidateSelector.Evaluate(node,ProxyCoreKind.Xray,true,true,history:history)[0].Compatible);
        } finally { File.Delete(path); }
    }
    [Theory] [InlineData(true,ProxyCoreKind.Hysteria)] [InlineData(false,ProxyCoreKind.SingBox)]
    public void HysteriaAvailability(bool installed, ProxyCoreKind expected)
    {
        Assert.Equal(expected,CoreCandidateSelector.Evaluate(new VlessNode { Protocol="hysteria2" },ProxyCoreKind.Auto,true,true,hysteriaInstalled:installed,allowHysteria:true)[0].Core);
    }
}
