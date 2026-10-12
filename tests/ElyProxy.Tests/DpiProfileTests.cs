using System.Diagnostics;
using ElyProxy.Core;
using ElyProxy.Models;
using ElyProxy.Services;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Tests;

public class DpiProfileTests
{
    private static XrayTunOptions Options => new("Probe", 1500, new[] { "1.1.1.1" }, false, Array.Empty<string>(), Array.Empty<string>());
    private static VlessNode Node => new()
    {
        Protocol = "vless", Address = "example.com", Port = 443,
        UUID = "11111111-1111-1111-1111-111111111111", Security = "tls", SNI = "example.com", Network = "tcp"
    };

    [Fact]
    public void FreshSettings_DoNotEnableAnyDpiOverride()
    {
        var settings = new AppSettings();
        Assert.Equal(FragmentModeKind.Off, settings.FragmentMode);
        Assert.False(settings.FragmentRecord);
        Assert.False(settings.TlsSpoofEnabled);
        Assert.Empty(settings.NoiseType);
        Assert.Empty(settings.DpiFingerprint);
    }

    [Fact]
    public void Presets_ClearIndependentSpoofAndNoise()
    {
        foreach (var profile in DpiProfileCatalog.All)
        {
            Assert.Equal(profile.Name, profile.ToString());
            var result = profile.Apply(Options with { TlsSpoofEnabled = true, NoiseType = "rand", DpiFingerprint = "firefox" });
            Assert.False(result.TlsSpoofEnabled);
            Assert.Empty(result.NoiseType);
            Assert.Equal("firefox", result.DpiFingerprint);
        }
    }

    [Fact]
    public void RecordsPreset_DoesNotEnableTcpFragmentation()
    {
        var options = DpiProfileCatalog.Find("records")!.Apply(Options);
        var json = JObject.Parse(new SingBoxConfigBuilder().BuildSystemProxy(Node, 1080, options));
        var tls = json["outbounds"]!.Children<JObject>().Single(o => (string?)o["tag"] == "proxy")["tls"]!;
        Assert.True((bool)tls["record_fragment"]!);
        Assert.Null(tls["fragment"]);
    }

    [Theory]
    [InlineData("tls", "tlsSettings")]
    [InlineData("reality", "realitySettings")]
    public void FingerprintOverride_DoesNotMutateSubscription(string security, string section)
    {
        var node = Node;
        node.Security = security;
        node.Fingerprint = "chrome";
        var json = JObject.Parse(new ConfigBuilder().Build(node, 1080, Options with { DpiFingerprint = "firefox" }, false));
        Assert.Equal("firefox", (string?)json["outbounds"]![0]!["streamSettings"]![section]!["fingerprint"]);
        Assert.Equal("chrome", node.Fingerprint);
        Assert.Single(json["inbounds"]!);
    }

    [Fact]
    public void Recommendation_RequiresAllChecksAndPrefersUnmodifiedConnection()
    {
        var off = DpiProfileCatalog.Find("off")!;
        var fast = DpiProfileCatalog.Find("fast")!;
        Assert.Null(DpiTuningService.Recommend(new[] { new DpiTrial(fast, 3, 20, "") }));
        Assert.Equal(off, DpiTuningService.Recommend(new[] { new DpiTrial(fast, 4, 20, ""), new DpiTrial(off, 4, 80, "") })!.Profile);
        Assert.Equal(fast, DpiTuningService.Recommend(new[] { new DpiTrial(fast, 4, 20, ""), new DpiTrial(off, 2, 10, "") })!.Profile);
    }

    [Fact]
    public async Task AllCompatibleProfiles_PassBundledCoreConfigChecks()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "ElyProxy.sln"))) root = root.Parent;
        Assert.NotNull(root);
        foreach (bool singBox in new[] { false, true })
        {
            string binary = Path.Combine(root!.FullName, singBox ? "sing-box/sing-box.exe" : "xray/xray.exe");
            if (!File.Exists(binary) && !singBox) continue;
            Assert.True(File.Exists(binary));
            foreach (var profile in DpiProfileCatalog.ForCore(singBox))
            {
                var options = profile.Apply(Options) with { DpiFingerprint = "firefox" };
                string json = singBox ? new SingBoxConfigBuilder().BuildSystemProxy(Node, 1080, options) : new ConfigBuilder().Build(Node, 1080, options, false);
                string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
                try
                {
                    File.WriteAllText(path, json);
                    using var process = Process.Start(new ProcessStartInfo(binary, (singBox ? "check -c " : "run -test -config ") + '"' + path + '"')
                    { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(10000)) { process.Kill(); Assert.Fail("Core validation timed out"); }
                    Assert.True(process.ExitCode == 0, profile.Id + ": " + await stdout + await stderr);
                }
                finally { File.Delete(path); }
            }
        }
    }
}
