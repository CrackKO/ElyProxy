using System.Diagnostics;
using ElyProxy.Core;
using ElyProxy.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ElyProxy.Tests;

public class ProxyChainTests
{
    private static VlessNode Entry() => new() { Protocol = "trojan", Address = "entry.example.com", Port = 443, Password = "entry-secret", Security = "tls", SNI = "entry.example.com" };
    private static XrayTunOptions Options(string protocol) => new("ElyTun", 1500, ["1.1.1.1"], false, [], [], ExitProxy: new VlessNode { Protocol = protocol, Address = "exit.example.com", Port = 8000, Username = "account", Password = "secret", Security = protocol == "http" ? "tls" : "none" });

    [Theory]
    [InlineData("socks")]
    [InlineData("http")]
    public async Task BothCoresAcceptExitThroughEntryWithoutFallback(string protocol)
    {
        var options = Options(protocol) with { FragmentEnabled = true };
        var xray = JObject.Parse(new ConfigBuilder().Build(Entry(), 1080, options, includeTunInbound: false));
        var xo = (JArray)xray["outbounds"]!;
        Assert.Equal("proxy", (string?)xo[0]!["tag"]);
        var exit = xo.OfType<JObject>().Single(o => (string?)o["tag"] == "proxy");
        var entry = xo.OfType<JObject>().Single(o => (string?)o["tag"] == "chain-entry");
        Assert.Equal("chain-entry", (string?)exit["streamSettings"]!["sockopt"]!["dialerProxy"]);
        Assert.Equal("fragment", (string?)entry["streamSettings"]!["sockopt"]!["dialerProxy"]);
        Assert.DoesNotContain(xo.OfType<JObject>(), o => o["proxySettings"] != null);

        foreach (var tun in new[] { false, true })
        {
            var json = tun ? new SingBoxConfigBuilder().BuildTun(Entry(), 1080, options) : new SingBoxConfigBuilder().BuildSystemProxy(Entry(), 1080, options);
            var sb = JObject.Parse(json);
            var so = ((JArray)sb["outbounds"]!).OfType<JObject>().ToArray();
            var final = so.Single(o => (string?)o["tag"] == "proxy");
            Assert.Equal("chain-entry", (string?)final["detour"]);
            Assert.Equal("exit.example.com", (string?)final["server"]);
            Assert.Equal("account", (string?)final["username"]);
            Assert.Equal("proxy", (string?)sb["route"]!["final"]);
            Assert.DoesNotContain(so, o => (string?)o["type"] is "selector" or "urltest");
            await CheckCore("sing-box", json);
        }
        await CheckCore("xray", xray.ToString());
    }

    [Fact]
    public void RejectsInvalidAndUnsupportedChains()
    {
        var invalid = Options("socks").ExitProxy!;
        invalid.Port = 0;
        Assert.Throws<NotSupportedException>(() => ProxyChainPolicy.Validate(Entry(), invalid));
        Assert.Throws<NotSupportedException>(() => ProxyChainPolicy.Validate(new VlessNode { Protocol = "wireguard" }, Options("socks").ExitProxy!));
        Assert.Throws<NotSupportedException>(() => ProxyChainPolicy.Validate(Entry(), Entry()));
    }

    [Theory]
    [InlineData("xray")]
    [InlineData("sing-box")]
    public async Task UnavailableExitIsRequestedThroughEntryAndNeverFallsBack(string core)
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "ElyProxy.sln"))) root = Directory.GetParent(root)!.FullName;
        var exe = Path.Combine(root, core, core + ".exe");
        if (!File.Exists(exe) && core == "xray") return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = timeout.Token;
        using var entryListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        entryListener.Start();
        int entryPort = ((System.Net.IPEndPoint)entryListener.LocalEndpoint).Port;
        using var reservation = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        reservation.Start();
        int localPort = ((System.Net.IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var primary = new VlessNode { Protocol = "socks", Address = "127.0.0.1", Port = entryPort };
        var options = Options("socks");
        options.ExitProxy!.Address = "192.0.2.25";
        var json = core == "xray" ? new ConfigBuilder().Build(primary, localPort, options, false)
            : new SingBoxConfigBuilder().BuildSystemProxy(primary, localPort, options);
        var file = Path.Combine(Path.GetTempPath(), "elyproxy-chain-live-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(file, json, ct);
        Process? process = null;
        try
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
            start.ArgumentList.Add("run"); start.ArgumentList.Add(core == "xray" ? "-config" : "-c"); start.ArgumentList.Add(file);
            process = Process.Start(start)!;
            _ = process.StandardOutput.ReadToEndAsync(ct); _ = process.StandardError.ReadToEndAsync(ct);
            using var client = new System.Net.Sockets.TcpClient();
            while (!client.Connected)
            {
                try { await client.ConnectAsync("127.0.0.1", localPort, ct); }
                catch (System.Net.Sockets.SocketException) { await Task.Delay(50, ct); }
            }
            var stream = client.GetStream();
            await stream.WriteAsync(new byte[] { 5, 1, 0 }, ct);
            var greeting = new byte[2]; await stream.ReadExactlyAsync(greeting, ct);
            Assert.Equal(new byte[] { 5, 0 }, greeting);
            // Request an internet destination from the local proxy.
            await stream.WriteAsync(new byte[] { 5, 1, 0, 1, 1, 1, 1, 1, 1, 187 }, ct);
            using var incoming = await entryListener.AcceptTcpClientAsync(ct);
            var entryStream = incoming.GetStream();
            var header = new byte[2]; await entryStream.ReadExactlyAsync(header, ct);
            var methods = new byte[header[1]]; await entryStream.ReadExactlyAsync(methods, ct);
            await entryStream.WriteAsync(new byte[] { 5, 0 }, ct);
            var request = new byte[10]; await entryStream.ReadExactlyAsync(request, ct);
            // The entry is asked for the EXIT proxy, never the final website.
            Assert.Equal(new byte[] { 5, 1, 0, 1, 192, 0, 2, 25, 31, 64 }, request);
            await entryStream.WriteAsync(new byte[] { 5, 5, 0, 1, 0, 0, 0, 0, 0, 0 }, ct);
            incoming.Close();
            var response = new byte[2];
            int count = await stream.ReadAsync(response, ct);
            if (count >= 2 && response[1] == 0)
            {
                // Xray acknowledges SOCKS CONNECT before the outbound handshake.
                // Success here is provisional; no application payload may arrive.
                var remainder = new byte[8]; await stream.ReadExactlyAsync(remainder, ct);
                try
                {
                    await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("GET / HTTP/1.0\r\n\r\n"), ct);
                    Assert.Equal(0, await stream.ReadAsync(new byte[64], ct));
                }
                catch (IOException) { /* Reset is also a closed failed connection. */ }
            }
            else Assert.True(count == 0 || count >= 2 && response[1] != 0);
            Assert.False(entryListener.Pending(), "Must not reconnect to the destination through the entry as a fallback.");
        }
        finally
        {
            if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            process?.Dispose(); File.Delete(file);
        }
    }

    private static async Task CheckCore(string core, string json)
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "ElyProxy.sln"))) root = Directory.GetParent(root)!.FullName;
        var exe = Path.Combine(root, core, core + ".exe");
        if (!File.Exists(exe) && core == "xray") return;
        var file = Path.Combine(Path.GetTempPath(), "elyproxy-chain-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(file, json);
        try
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
            if (core == "xray") { start.ArgumentList.Add("run"); start.ArgumentList.Add("-test"); start.ArgumentList.Add("-config"); }
            else { start.ArgumentList.Add("check"); start.ArgumentList.Add("-c"); }
            start.ArgumentList.Add(file);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(process.ExitCode == 0, await stdout + await stderr);
        }
        finally { File.Delete(file); }
    }
}

