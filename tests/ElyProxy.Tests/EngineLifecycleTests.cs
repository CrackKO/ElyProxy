using ElyProxy.Core;
using ElyProxy.Models;
using ElyProxy.Services;
using Xunit;
namespace ElyProxy.Tests;
public class EngineLifecycleTests
{
    [Fact]
    public async Task CancelledStartupIsDrainedBeforeCleanupAndNextOwner()
    {
        var launched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        bool running = false; bool stopped = false;
        var operation = CoreStartupOperation.RunAsync(async () => { launched.SetResult(); await release.Task; running = true; },
            () => { running = false; stopped = true; return Task.CompletedTask; }, cancellation.Token, TimeSpan.FromSeconds(5));
        await launched.Task; cancellation.Cancel();
        Assert.False(operation.IsCompleted); release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.True(stopped); Assert.False(running);
        running = true; await Task.Yield(); Assert.True(running);
    }
    [Fact] public async Task StartupTimeoutCleansUp()
    {
        bool stopped = false;
        await Assert.ThrowsAsync<TimeoutException>(() => CoreStartupOperation.RunAsync(() => Task.Delay(50),
            () => { stopped=true; return Task.CompletedTask; }, default, TimeSpan.FromMilliseconds(5)));
        Assert.True(stopped);
    }
    [Fact] public void StartsAreDifferentFromValidatedSuccessAndLegacyLoads()
    {
        var old=System.Text.Json.JsonSerializer.Deserialize<CorePerformanceRecord>("{\"Successes\":20}")!;
        Assert.Equal(0,old.ValidatedConnections);
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        try {
            var history=new CorePerformanceHistoryService(path);
            history.Record("key",true,100,validated:false,validationMs:8000);
            var started=history.Get("key")!;
            Assert.Equal(1,started.StartedConnections); Assert.Equal(0,started.ValidatedConnections);
            history.Record("key",true,100,validated:true,validationMs:100);
            var validated=history.Get("key")!;
            Assert.Equal(1,validated.ValidatedConnections); Assert.True(validated.Reliability>started.Reliability);
            Assert.Equal(0,validated.Failures);
        } finally { File.Delete(path); }
    }
    [Fact] public void MigrationPreservesManualAndNewUsersGetAuto()
    {
        Assert.Equal(ProxyCoreKind.Auto,CoreSettingsMigration.NewInstallation().ProxyCore);
        Assert.Equal(TunCoreKind.Auto,CoreSettingsMigration.NewInstallation().TunCore);
        Assert.Equal(ProxyCoreKind.Xray,CoreSettingsMigration.ResolveLegacyProxy(new AppSettings { ProxyCore=ProxyCoreKind.Xray }));
        Assert.Equal(ProxyCoreKind.SingBox,CoreSettingsMigration.ResolveLegacyProxy(new AppSettings { ProxyCore=ProxyCoreKind.SingBox }));
    }
}


