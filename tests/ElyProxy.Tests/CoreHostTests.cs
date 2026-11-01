using System.Diagnostics;
using ElyProxy.Core;
using ElyProxy.Models;
using Xunit;

namespace ElyProxy.Tests;

public class CoreHostTests
{
    [Theory]
    [InlineData("sing-box")]
    [InlineData("xray")]
    public async Task IsolatedHostStopsRealCoreAndReleasesPort(string core)
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "ElyProxy.sln"))) root = Directory.GetParent(root)!.FullName;
        var exe = Path.Combine(root, core, core + ".exe");
        if (!File.Exists(exe) && core == "xray") return;
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string helper = Path.Combine(root, "ElyProxy.Helper", "bin", configuration, "net8.0-windows", "ElyProxy.Helper.exe");
        for (int cycle = 0; cycle < 3; cycle++)
        {
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start(); int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var node = new VlessNode { Protocol = "socks", Address = "127.0.0.1", Port = 9 };
            var options = new XrayTunOptions("ProxyPro", 1500, ["1.1.1.1"], false, [], []);
            var json = core == "sing-box" ? new SingBoxConfigBuilder().BuildSystemProxy(node, port, options) : new ConfigBuilder().Build(node, port);
            var file = Path.Combine(Path.GetTempPath(), "elyproxy-host-" + Guid.NewGuid().ToString("N") + ".json");
            await File.WriteAllTextAsync(file, json);
            using var p = Process.Start(CoreHostLaunch.Create(helper, exe, "run " + (core == "xray" ? "-config" : "-c") + " \"" + file + "\"", root))!;
            ProcessJob.Assign(p);
            try
            {
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var output = Task.Run(async () => { while (await p.StandardOutput.ReadLineAsync() is { } line) if (line == "[elyproxy-host-ready]") ready.TrySetResult(); });
                var errors = p.StandardError.ReadToEndAsync();
                var first = await Task.WhenAny(ready.Task, p.WaitForExitAsync(), Task.Delay(15000));
                Assert.True(first == ready.Task, "Core host not ready: " + (p.HasExited ? await errors : "timeout"));
                await OwnedProcessStopper.StopAsync(p);
                Assert.Equal(0, p.ExitCode);
                await output;
                listener.Start(); listener.Stop();
            }
            finally
            {
                if (!p.HasExited) { p.Kill(true); await p.WaitForExitAsync(); }
                File.Delete(file);
            }
        }
    }
}
