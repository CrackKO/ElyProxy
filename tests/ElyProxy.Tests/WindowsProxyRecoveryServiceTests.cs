using ElyProxy.Services;
using Microsoft.Win32;

namespace ElyProxy.Tests;

public sealed class WindowsProxyRecoveryServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ElyProxy-ProxyRecoveryTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void RecoverPendingAtStartup_RestoresPreviousPacSettings()
    {
        var proxy = new FakeWindowsProxyService { CurrentSettings = OriginalSettings() };
        var firstProcess = new WindowsProxyRecoveryService(proxy, new StorageService(_directory));
        const string pacUrl = "http://127.0.0.1:18080/proxy.pac";
        firstProcess.PrepareForPac(pacUrl);
        proxy.CurrentSettings = PacSettings(pacUrl);

        var restartedProcess = new WindowsProxyRecoveryService(proxy, new StorageService(_directory));
        WindowsProxyRecoveryResult result = restartedProcess.RecoverPendingAtStartup();

        Assert.Equal(WindowsProxyRecoveryStatus.Restored, result.Status);
        Assert.Equal(OriginalSettings(), proxy.CurrentSettings);
        Assert.False(File.Exists(Path.Combine(_directory, "windows-proxy-recovery.json")));
    }

    [Fact]
    public void RecoverPendingAtStartup_LeavesUserModifiedProxyUntouched()
    {
        var proxy = new FakeWindowsProxyService { CurrentSettings = OriginalSettings() };
        new WindowsProxyRecoveryService(proxy, new StorageService(_directory))
            .PrepareForPac("http://127.0.0.1:18080/proxy.pac");
        var userSettings = PacSettings("http://127.0.0.1:9999/other.pac");
        proxy.CurrentSettings = userSettings;

        WindowsProxyRecoveryResult result = new WindowsProxyRecoveryService(proxy, new StorageService(_directory))
            .RecoverPendingAtStartup();

        Assert.Equal(WindowsProxyRecoveryStatus.UserSettingsPreserved, result.Status);
        Assert.Equal(userSettings, proxy.CurrentSettings);
    }

    [Fact]
    public void RecoverPendingAtStartup_RestoresOnlyPacAndPreservesOtherUserProxyChanges()
    {
        var proxy = new FakeWindowsProxyService { CurrentSettings = OriginalSettings() };
        new WindowsProxyRecoveryService(proxy, new StorageService(_directory))
            .PrepareForPac("http://127.0.0.1:18080/proxy.pac");
        proxy.CurrentSettings = PacSettings("http://127.0.0.1:18080/proxy.pac") with
        {
            ProxyServer = String("user.proxy.local:9000")
        };

        WindowsProxyRecoveryResult result = new WindowsProxyRecoveryService(proxy, new StorageService(_directory))
            .RecoverPendingAtStartup();

        Assert.Equal(WindowsProxyRecoveryStatus.Restored, result.Status);
        Assert.Equal("https://company.example.org/proxy.pac", proxy.CurrentSettings.AutoConfigUrl?.StringValue);
        Assert.Equal("user.proxy.local:9000", proxy.CurrentSettings.ProxyServer?.StringValue);
    }

    [Fact]
    public void RecoverPendingAtStartup_RestoresManualProxySettings()
    {
        var proxy = new FakeWindowsProxyService { CurrentSettings = OriginalSettings() };
        new WindowsProxyRecoveryService(proxy, new StorageService(_directory))
            .PrepareForManualProxy(1080, ["*.example.org"]);
        proxy.CurrentSettings = ManualProxySettings(1080, ["*.example.org"]);

        WindowsProxyRecoveryResult result = new WindowsProxyRecoveryService(proxy, new StorageService(_directory))
            .RecoverPendingAtStartup();

        Assert.Equal(WindowsProxyRecoveryStatus.Restored, result.Status);
        Assert.Equal(OriginalSettings(), proxy.CurrentSettings);
    }

    [Fact]
    public void PrepareForPac_RetainsOriginalSnapshotWhenPacUrlChanges()
    {
        var proxy = new FakeWindowsProxyService { CurrentSettings = OriginalSettings() };
        var recovery = new WindowsProxyRecoveryService(proxy, new StorageService(_directory));
        recovery.PrepareForPac("http://127.0.0.1:18080/proxy.pac");
        proxy.CurrentSettings = PacSettings("http://127.0.0.1:18080/proxy.pac");
        recovery.PrepareForPac("http://127.0.0.1:18081/proxy.pac");
        proxy.CurrentSettings = PacSettings("http://127.0.0.1:18080/proxy.pac");

        WindowsProxyRecoveryResult result = new WindowsProxyRecoveryService(proxy, new StorageService(_directory))
            .RecoverPendingAtStartup();

        Assert.Equal(WindowsProxyRecoveryStatus.Restored, result.Status);
        Assert.Equal(OriginalSettings(), proxy.CurrentSettings);
    }

    [Fact]
    public void PrepareForManualProxy_DoesNotReplacePendingRecoveryFromAnotherMode()
    {
        var proxy = new FakeWindowsProxyService { CurrentSettings = OriginalSettings() };
        new WindowsProxyRecoveryService(proxy, new StorageService(_directory))
            .PrepareForPac("http://127.0.0.1:18080/proxy.pac");

        var restartedService = new WindowsProxyRecoveryService(proxy, new StorageService(_directory));

        Assert.Throws<InvalidOperationException>(() => restartedService.PrepareForManualProxy(1080, []));
        Assert.True(File.Exists(Path.Combine(_directory, "windows-proxy-recovery.json")));
    }

    [Fact]
    public void PrepareForPac_DoesNotReuseJournalAfterUserChangesPacAddress()
    {
        var proxy = new FakeWindowsProxyService { CurrentSettings = OriginalSettings() };
        var recovery = new WindowsProxyRecoveryService(proxy, new StorageService(_directory));
        recovery.PrepareForPac("http://127.0.0.1:18080/proxy.pac");
        var userSettings = PacSettings("http://127.0.0.1:9999/user.pac");
        proxy.CurrentSettings = userSettings;

        Assert.Throws<InvalidOperationException>(() => recovery.PrepareForPac("http://127.0.0.1:18081/proxy.pac"));
        Assert.Equal(userSettings, proxy.CurrentSettings);
        Assert.True(File.Exists(Path.Combine(_directory, "windows-proxy-recovery.json")));
    }

    private static WindowsProxySnapshot OriginalSettings() => new(
        Dword(1),
        String("proxy.example.org:8080"),
        String("localhost;*.internal"),
        String("https://company.example.org/proxy.pac"));

    private static WindowsProxySnapshot PacSettings(string url) => new(
        Dword(1),
        String("proxy.example.org:8080"),
        String("localhost;*.internal"),
        String(url));

    private static WindowsProxySnapshot ManualProxySettings(int port, IEnumerable<string> rules) => new(
        Dword(1),
        String($"http=127.0.0.1:{port};https=127.0.0.1:{port};socks=127.0.0.1:{port}"),
        String(string.Join(';', rules.Where(rule => !rule.Contains('/')).Append("<local>"))),
        null);

    private static WindowsProxyValueSnapshot Dword(int value) => new()
    {
        Kind = RegistryValueKind.DWord,
        DwordValue = value
    };

    private static WindowsProxyValueSnapshot String(string value) => new()
    {
        Kind = RegistryValueKind.String,
        StringValue = value
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeWindowsProxyService : IWindowsProxyService
    {
        private readonly WindowsProxyService _policy = new();

        public WindowsProxySnapshot CurrentSettings { get; set; } = new(null, null, null, null);

        public string? GetAutoConfigUrl() => CurrentSettings.AutoConfigUrl?.StringValue;

        public WindowsProxySnapshot CaptureCurrentSettings() => CurrentSettings;

        public bool IsOwnedByPac(WindowsProxySnapshot current, IEnumerable<string> ownedPacUrls) =>
            _policy.IsOwnedByPac(current, ownedPacUrls);

        public bool IsOwnedByManualProxy(WindowsProxySnapshot current, int port, IEnumerable<string> bypassRules) =>
            _policy.IsOwnedByManualProxy(current, port, bypassRules);

        public void EnablePac(string pacUrl) => CurrentSettings = CurrentSettings with { AutoConfigUrl = String(pacUrl) };

        public void DisablePac(string pacUrl, string? restoreAutoConfigUrl) =>
            CurrentSettings = CurrentSettings with { AutoConfigUrl = restoreAutoConfigUrl == null ? null : String(restoreAutoConfigUrl) };

        public void EnableManualProxy(int port, IEnumerable<string> bypassRules) =>
            CurrentSettings = ManualProxySettings(port, bypassRules);

        public void RestoreManualProxySettings(WindowsProxySnapshot snapshot) => CurrentSettings = snapshot;

        public void RestorePacSettings(WindowsProxySnapshot snapshot) => CurrentSettings = CurrentSettings with { AutoConfigUrl = snapshot.AutoConfigUrl };
    }
}
