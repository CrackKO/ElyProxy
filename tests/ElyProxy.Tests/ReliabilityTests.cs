using System.IO.Pipes;
using System.Text.Json;
using ElyProxy.Core;
using ElyProxy.Models;
using ElyProxy.Services;
using Xunit;

namespace ElyProxy.Tests;

public class ReliabilityTests
{
    [Fact]
    public async Task OldHelperIsRejectedBeforeAnyCoreIsStarted()
    {
        var name = "ElyProxyLegacyTest-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var connection = server.WaitForConnectionAsync();
        using var client = new HelperProcessManager(name);
        Assert.True(await client.TryConnectAsync()); await connection;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.StartAsync("sing-box.exe", "run"));
        Assert.Contains("Обновите службу", error.Message);
        Assert.False(client.IsRunning);
    }

    [Fact]
    public void HttpsProxyInXrayHasTlsTransport()
    {
        var node = new ParserService().ParseUri("https://account:secret@proxy.example:8443")!;
        var json = Newtonsoft.Json.Linq.JObject.Parse(new ConfigBuilder().Build(node));
        var stream = json["outbounds"]![0]!["streamSettings"]!;
        Assert.Equal("tls", (string?)stream["security"]);
        Assert.Equal("proxy.example", (string?)stream["tlsSettings"]!["serverName"]);
    }
    [Fact]
    public async Task CleanupAttemptsEveryResourceAfterFailures()
    {
        var calls = new List<int>();
        var error = await Assert.ThrowsAsync<AggregateException>(() => LifecycleCleanup.RunAsync(
            () => { calls.Add(1); throw new IOException("first"); },
            () => { calls.Add(2); return Task.CompletedTask; },
            () => { calls.Add(3); throw new IOException("last"); }));
        Assert.Equal(new[] { 1, 2, 3 }, calls);
        Assert.Equal(2, error.InnerExceptions.Count);
    }

    [Fact]
    public async Task HelperStopWaitsForCleanupAcknowledgementAndCanRetryFailure()
    {
        var pipeName = "ElyProxyTest-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var connected = server.WaitForConnectionAsync();
        using var manager = new HelperProcessManager(pipeName);
        Assert.True(await manager.TryConnectAsync()); await connected;
        using var reader = new StreamReader(server);
        using var writer = new StreamWriter(server) { AutoFlush = true };
        await writer.WriteLineAsync("{\"type\":\"hello\",\"message\":\"stop-ack-v2\"}");
        var start = manager.StartAsync("sing-box.exe", "run");
        using var command = JsonDocument.Parse((await reader.ReadLineAsync())!);
        string id = command.RootElement.GetProperty("id").GetString()!;
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "started", id, pid = 123 }));
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "stdout", id, line = "[elyproxy-host-ready]" }));
        await start;
        var stop = manager.StopAsync(); await reader.ReadLineAsync();
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "exit", id, code = 0 }));
        await Task.Delay(100);
        Assert.False(stop.IsCompleted);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "error", id, message = "cleanup failed" }));
        await Assert.ThrowsAsync<IOException>(() => stop);
        var retry = manager.StopAsync(); await reader.ReadLineAsync();
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "stopped", id }));
        await retry;
        Assert.False(manager.IsRunning);
    }

    [Fact]
    public void ActiveServerSurvivesRefreshWithoutMarkingChangedConfigurationConnected()
    {
        var active = new VlessNode { Name = "Germany", Address = "192.0.2.1", Port = 443, UUID = "old" };
        var same = new VlessNode { Name = "Renamed", Address = active.Address, Port = 443, UUID = "old", LibraryId = active.LibraryId };
        Assert.True(ActiveServerIdentity.Matches(active, same));
        same.UUID = "new";
        Assert.False(ActiveServerIdentity.Matches(active, same));
        var neighbor = new VlessNode { Name = "Other", Address = active.Address, Port = 443, UUID = "old" };
        Assert.False(ActiveServerIdentity.Matches(active, neighbor));
    }

    [Theory]
    [InlineData(System.Net.Http.HttpRequestError.NameResolutionError, "DNS")]
    [InlineData(System.Net.Http.HttpRequestError.SecureConnectionError, "TLS")]
    [InlineData(System.Net.Http.HttpRequestError.ConnectionError, "TCP")]
    public void ErrorsIdentifyActualNetworkLayer(System.Net.Http.HttpRequestError kind, string layer) =>
        Assert.StartsWith(layer, NetworkErrorMessage.Describe(new HttpRequestException(kind, "host:443")));

    [Fact]
    public void DiagnosticLogsDoNotStoreSubscriptionOrProxyCredentials()
    {
        var value = LifecycleLog.Redact("error https://provider.example/sub/SECRET?token=PASSWORD socks5://user:secret@host:1080");
        Assert.DoesNotContain("SECRET", value); Assert.DoesNotContain("secret", value); Assert.DoesNotContain("PASSWORD", value);
        Assert.Contains("[URL]", value);
    }
}
