using System.Net;
using System.Net.Sockets;
using ElyProxy.Models;
using ElyProxy.Services;
using Microsoft.Win32;

namespace ElyProxy.Tests;

public sealed class ConnectionDiagnosticsServiceTests
{
    [Fact]
    public async Task RunAsync_SeparatesCoreProxyModeAndExternalRouteChecks()
    {
        int unusedPort = ReservePort();
        const string pacUrl = "http://127.0.0.1:18080/proxy.pac";
        var proxy = new FakeProxyService
        {
            CurrentSettings = new WindowsProxySnapshot(
                null,
                null,
                null,
                new WindowsProxyValueSnapshot { Kind = RegistryValueKind.String, StringValue = pacUrl })
        };

        ConnectionDiagnosticsResult result = await new ConnectionDiagnosticsService(proxy).RunAsync(
            coreRunning: true,
            unusedPort,
            "Proxy Lite",
            pacUrl,
            Array.Empty<string>());

        Assert.Equal(4, result.Checks.Count);
        Assert.True(result.Checks.Single(check => check.Name == "Ядро").Passed);
        Assert.False(result.Checks.Single(check => check.Name == "Локальный SOCKS5").Passed);
        Assert.Contains("Переподключитесь", result.Checks.Single(check => check.Name == "Локальный SOCKS5").SuggestedAction);
        Assert.True(result.Checks.Single(check => check.Name == "Режим Windows").Passed);
        Assert.False(result.Checks.Single(check => check.Name == "Внешний маршрут").Passed);
        Assert.Contains("Сначала восстановите локальный порт", result.Checks.Single(check => check.Name == "Внешний маршрут").SuggestedAction);
        Assert.DoesNotContain("11111111-1111-1111-1111-111111111111", result.ToSafeReport(), StringComparison.OrdinalIgnoreCase);
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class FakeProxyService : IWindowsProxyService
    {
        public WindowsProxySnapshot CurrentSettings { get; set; } = new(null, null, null, null);

        public string? GetAutoConfigUrl() => CurrentSettings.AutoConfigUrl?.StringValue;

        public WindowsProxySnapshot CaptureCurrentSettings() => CurrentSettings;

        public bool IsOwnedByPac(WindowsProxySnapshot current, IEnumerable<string> ownedPacUrls) =>
            current.AutoConfigUrl?.StringValue is { } active && ownedPacUrls.Contains(active, StringComparer.OrdinalIgnoreCase);

        public bool IsOwnedByManualProxy(WindowsProxySnapshot current, int port, IEnumerable<string> bypassRules) => false;

        public void EnablePac(string pacUrl) { }

        public void DisablePac(string pacUrl, string? restoreAutoConfigUrl) { }

        public void EnableManualProxy(int port, IEnumerable<string> bypassRules) { }

        public void RestoreManualProxySettings(WindowsProxySnapshot snapshot) { }

        public void RestorePacSettings(WindowsProxySnapshot snapshot) { }
    }
}
