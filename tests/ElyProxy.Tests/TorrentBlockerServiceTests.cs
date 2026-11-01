using ElyProxy.Services;
using Xunit;

namespace ElyProxy.Tests;

public class TorrentBlockerServiceTests
{
    [Theory]
    [InlineData("qBittorrent", true)]
    [InlineData("uTorrent", true)]
    [InlineData("transmission-daemon", true)]
    [InlineData("aria2c", true)]
    [InlineData("chrome", false)]
    [InlineData("java", false)]
    [InlineData("qbittorrent-helper", false)]
    [InlineData("my-torrent-document", false)]
    public void RecognizesExactClientNamesOnly(string name, bool expected) =>
        Assert.Equal(expected, TorrentBlockerService.IsTorrentClient(name));

    [Fact]
    public async Task DisabledGuardDoesNotEnumerateOrKill()
    {
        using var guard = new TorrentBlockerService(_ => { }, () => throw new Exception("Must not enumerate"));
        Assert.True(await guard.ScanAsync());
    }

    [Fact]
    public async Task EnabledGuardClosesClientsAndLeavesOtherAppsAlone()
    {
        var client = new FakeProcess("qbittorrent");
        var browser = new FakeProcess("chrome");
        using var guard = new TorrentBlockerService(_ => { }, () => new[] { client, browser });
        guard.SetEnabled(true);
        Assert.True(await guard.ScanAsync());
        guard.SetEnabled(false);
        Assert.True(client.HasExited);
        Assert.False(browser.HasExited);
        Assert.True(client.Disposed);
        Assert.True(browser.Disposed);
    }

    [Fact]
    public async Task AccessDeniedFailsPreflightAndLogsReason()
    {
        var logs = new List<string>();
        var client = new FakeProcess("utorrent") { Denied = true };
        using var guard = new TorrentBlockerService(logs.Add, () => new[] { client });
        guard.SetEnabled(true);
        Assert.False(await guard.ScanAsync());
        guard.SetEnabled(false);
        Assert.False(client.HasExited);
        Assert.Single(logs);
        Assert.Contains("utorrent", logs[0]);
    }

    [Fact]
    public async Task GuardStopsNewInstancesAndStopsKillingWhenDisabled()
    {
        FakeProcess current = new("deluge");
        using var guard = new TorrentBlockerService(_ => { }, () => new[] { current });
        guard.SetEnabled(true);
        await guard.ScanAsync();
        current = new FakeProcess("deluge");
        Assert.True(await guard.ScanAsync());
        Assert.True(current.HasExited);
        guard.SetEnabled(false);
        current = new FakeProcess("deluge");
        Assert.True(await guard.ScanAsync());
        Assert.False(current.HasExited);
    }

    private sealed class FakeProcess(string name) : TorrentBlockerService.ITorrentProcess
    {
        public string Name => name;
        public bool HasExited { get; private set; }
        public bool Denied { get; init; }
        public bool Disposed { get; private set; }
        public void Kill()
        {
            if (Denied) throw new System.ComponentModel.Win32Exception(5);
            if (HasExited) throw new InvalidOperationException();
            HasExited = true;
        }
        public bool WaitForExit(int milliseconds) => HasExited;
        public void Dispose() => Disposed = true;
    }
}
