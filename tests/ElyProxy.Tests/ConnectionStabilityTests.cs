using ElyProxy.Core;
using ElyProxy.Models;
using ElyProxy.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ElyProxy.Tests;

public class ConnectionStabilityTests
{
    private static VlessNode Node(string protocol = "vless", string network = "tcp") => new()
    {
        Protocol = protocol, Network = network, Address = "example.com", Port = 443,
        UUID = "11111111-1111-1111-1111-111111111111", Security = "tls", SNI = "example.com"
    };
    private static XrayTunOptions Options => new("ElyTun-1234abcd", 1500, ["1.1.1.1"], false, [], []);

    [Theory]
    [InlineData("vless", "tcp", ProxyCoreKind.SingBox)]
    [InlineData("vless", "xhttp", ProxyCoreKind.Xray)]
    [InlineData("vless", "kcp", ProxyCoreKind.Xray)]
    [InlineData("tuic", "tcp", ProxyCoreKind.SingBox)]
    [InlineData("hysteria2", "tcp", ProxyCoreKind.SingBox)]
    [InlineData("ssh", "tcp", ProxyCoreKind.SingBox)]
    public void AutoIsDeterministic(string protocol, string network, ProxyCoreKind expected) =>
        Assert.Equal(expected, CoreSelectionPolicy.Resolve(Node(protocol, network), ProxyCoreKind.Auto, true, true).Core);

    [Fact]
    public void AutoDoesNotDisableRequiredFeatures()
    {
        Assert.Equal(ProxyCoreKind.Xray, CoreSelectionPolicy.Resolve(Node(), ProxyCoreKind.Auto, true, true, requiresXray: true).Core);
        Assert.Throws<NotSupportedException>(() => CoreSelectionPolicy.Resolve(Node(), ProxyCoreKind.Auto, true, true, true, true));
        Assert.Throws<NotSupportedException>(() => CoreSelectionPolicy.Resolve(Node("tuic"), ProxyCoreKind.Xray, true, true));
        Assert.Throws<NotSupportedException>(() => CoreSelectionPolicy.Resolve(Node(), ProxyCoreKind.Auto, false, false));
    }

    [Fact]
    public void VlessEncryptionRequiresXray()
    {
        var node = Node(); node.Encryption = "mlkem768x25519plus.native.0rtt.test";
        Assert.Equal(ProxyCoreKind.Xray, CoreSelectionPolicy.Resolve(node, ProxyCoreKind.Auto, true, true).Core);
    }

    [Fact]
    public async Task ShutdownInvalidatesQueuedWorkAndSerializesCleanup()
    {
        var coordinator = new ConnectionCoordinator();
        int generation = coordinator.Generation;
        await coordinator.WaitAsync();
        var second = coordinator.WaitAsync();
        Assert.False(second.IsCompleted);
        coordinator.BeginShutdown();
        Assert.False(coordinator.IsCurrent(generation));
        coordinator.Release(); await second;
        Assert.True(coordinator.ShuttingDown);
        coordinator.Release();
    }

    [Fact]
    public void DpiMigrationKeepsCoreSpecificFieldsAndDoesNotEnableNewMethods()
    {
        var settings = new AppSettings { FragmentMode = FragmentModeKind.Custom, FragmentLength = "50-80", NoiseType = "rand", NoisePacket = "10-20", FragmentRecord = true };
        var migrated = DpiSettings.Migrate(settings);
        var xray = migrated.Apply(Options, false);
        var singBox = migrated.Apply(Options, true);
        Assert.Equal("50-80", xray.FragmentLength); Assert.Equal("rand", xray.NoiseType);
        Assert.False(xray.RecordFragment); Assert.True(singBox.RecordFragment);
        Assert.Empty(singBox.NoiseType); Assert.False(singBox.TlsSpoofEnabled);
        var copy = migrated with { Xray = migrated.Xray with { Fragment = false } };
        Assert.True(migrated.Xray.Fragment); Assert.False(copy.Xray.Fragment);
    }

    [Fact]
    public void NoiseWorksWithoutTcpFragmentationAndDoesNotModifyDirectOutbound()
    {
        var json = JObject.Parse(new ConfigBuilder().Build(Node(), 1080,
            Options with { NoiseType = "rand", NoisePacket = "10-20" }, false));
        var outbounds = json["outbounds"]!.Children<JObject>().ToArray();
        var noise = outbounds.Single(o => (string?)o["tag"] == "fragment");
        Assert.Null(noise["settings"]!["fragment"]);
        Assert.NotNull(noise["settings"]!["noises"]);
        Assert.Null(outbounds.Single(o => (string?)o["tag"] == "direct")["settings"]);
    }

    [Theory]
    [InlineData("0", "1-20", true)]
    [InlineData("-1", "1-20", false)]
    [InlineData("5001", "1-20", false)]
    [InlineData("10", "40-20", false)]
    public void DpiRangesAreValidatedBeforeNetworkChanges(string interval, string length, bool valid)
    {
        var options = Options with { FragmentEnabled = true, FragmentInterval = interval, FragmentLength = length };
        if (valid) DpiSettingsValidator.Validate(options, Node(), false);
        else Assert.Throws<InvalidDataException>(() => DpiSettingsValidator.Validate(options, Node(), false));
    }

    [Fact]
    public void TunRequiresDefaultOrBothHalfRoutes()
    {
        Assert.True(ConnectionReadiness.HasDefaultRoutes(["0.0.0.0/0"]));
        Assert.True(ConnectionReadiness.HasDefaultRoutes(["0.0.0.0/1", "128.0.0.0/1"]));
        Assert.False(ConnectionReadiness.HasDefaultRoutes(["0.0.0.0/1"]));
        Assert.False(ConnectionReadiness.HasDefaultRoutes(["10.0.0.0/24"]));
    }

    [Fact]
    public void XrayHttpAndSocksListenOnSeparateLoopbackPorts()
    {
        var json = JObject.Parse(new ConfigBuilder().Build(Node(), 1080, Options with { HttpPort = 18081 }, false));
        var inbounds = json["inbounds"]!.Children<JObject>().ToArray();
        Assert.Equal(1080, (int)inbounds.Single(i => (string?)i["protocol"] == "socks")["port"]!);
        var http = inbounds.Single(i => (string?)i["protocol"] == "http");
        Assert.Equal(18081, (int)http["port"]!); Assert.Equal("127.0.0.1", (string?)http["listen"]);
    }
}
