using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using ElyProxy.Services;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Tests;

public sealed class GeoDataUpdateServiceTests : IDisposable
{
    private const string GeoIpUrl = "https://github.com/Loyalsoldier/v2ray-rules-dat/releases/download/202610100000/geoip.dat";
    private const string GeoSiteUrl = "https://github.com/Loyalsoldier/v2ray-rules-dat/releases/download/202610100000/geosite.dat";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ElyProxy-GeoUpdateTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task UpdateXrayGeoAsync_InstallsBothFilesAndCleansBackups()
    {
        var target = PrepareTarget(("geoip.dat", "old-ip"));
        using var service = CreateService(target, Payloads(), digest: true);
        var progress = new RecordingProgress();

        var report = await service.UpdateXrayGeoAsync(progress);

        Assert.Equal("new-ip", File.ReadAllText(Path.Combine(target, "geoip.dat")));
        Assert.Equal("new-site", File.ReadAllText(Path.Combine(target, "geosite.dat")));
        Assert.Empty(Directory.GetFiles(target, "*.bak"));
        Assert.Contains(target, report);
        Assert.Equal(50d, progress.Values[0]);
        Assert.Equal(100d, progress.Values[^1]);
    }

    [Fact]
    public async Task UpdateXrayGeoAsync_DigestMismatch_ChangesNothing()
    {
        var target = PrepareTarget(("geoip.dat", "old-ip"), ("geosite.dat", "old-site"));
        using var service = CreateService(target, Payloads(), digest: true, brokenDigestFor: "geosite.dat");

        await Assert.ThrowsAsync<InvalidDataException>(() => service.UpdateXrayGeoAsync());

        Assert.Equal("old-ip", File.ReadAllText(Path.Combine(target, "geoip.dat")));
        Assert.Equal("old-site", File.ReadAllText(Path.Combine(target, "geosite.dat")));
    }

    [Fact]
    public async Task UpdateXrayGeoAsync_InstallFailure_RestoresBackups()
    {
        var target = PrepareTarget(("geoip.dat", "old-ip"), ("geosite.dat", "old-site"));
        using var service = CreateService(target, Payloads(), digest: false);

        // Lock geosite.dat so the backup/install copy fails after geoip.dat was replaced.
        using (new FileStream(Path.Combine(target, "geosite.dat"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => service.UpdateXrayGeoAsync());
        }

        Assert.Equal("old-ip", File.ReadAllText(Path.Combine(target, "geoip.dat")));
        Assert.Equal("old-site", File.ReadAllText(Path.Combine(target, "geosite.dat")));
    }

    [Fact]
    public async Task UpdateXrayGeoAsync_MissingAssetInRelease_Throws()
    {
        var target = PrepareTarget(("geoip.dat", "old-ip"));
        using var service = CreateService(target, Payloads(), digest: false, omitGeoSite: true);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.UpdateXrayGeoAsync());

        Assert.Equal("old-ip", File.ReadAllText(Path.Combine(target, "geoip.dat")));
    }

    [Fact]
    public async Task UpdateXrayGeoAsync_InvalidAssetUrl_Throws()
    {
        var target = PrepareTarget();
        using var service = CreateService(target, Payloads(), digest: false, geoIpUrl: "http://github.com/geoip.dat");

        await Assert.ThrowsAsync<InvalidDataException>(() => service.UpdateXrayGeoAsync());
    }

    private string PrepareTarget(params (string Name, string Content)[] files)
    {
        var target = Path.Combine(_directory, "xray");
        Directory.CreateDirectory(target);
        foreach (var (name, content) in files)
        {
            File.WriteAllText(Path.Combine(target, name), content);
        }

        return target;
    }

    private static Dictionary<string, byte[]> Payloads() => new()
    {
        ["geoip.dat"] = Encoding.UTF8.GetBytes("new-ip"),
        ["geosite.dat"] = Encoding.UTF8.GetBytes("new-site")
    };

    private GeoDataUpdateService CreateService(
        string target,
        Dictionary<string, byte[]> payloads,
        bool digest,
        string? brokenDigestFor = null,
        string geoIpUrl = GeoIpUrl,
        bool omitGeoSite = false)
    {
        string? DigestFor(string name)
        {
            if (!digest)
            {
                return null;
            }

            return brokenDigestFor == name
                ? "sha256:" + new string('a', 64)
                : Sha(payloads.GetValueOrDefault(name) ?? Array.Empty<byte>());
        }

        var assets = new JArray
        {
            Asset("geoip.dat", geoIpUrl, DigestFor("geoip.dat"))
        };
        if (!omitGeoSite)
        {
            assets.Add(Asset("geosite.dat", GeoSiteUrl, DigestFor("geosite.dat")));
        }

        var release = new JObject { ["assets"] = assets }.ToString();

        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri?.Host == "api.github.com")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(release, Encoding.UTF8, "application/json")
                };
            }

            var fileName = Path.GetFileName(request.RequestUri?.AbsolutePath ?? string.Empty);
            return payloads.TryGetValue(fileName, out var payload)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        return new GeoDataUpdateService(new HttpClient(handler), target);
    }

    private static JObject Asset(string name, string url, string? digest) => new()
    {
        ["name"] = name,
        ["browser_download_url"] = url,
        ["digest"] = digest
    };

    private static string Sha(byte[] payload) => "sha256:" + Convert.ToHexString(SHA256.HashData(payload));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class RecordingProgress : IProgress<double>
    {
        public List<double> Values { get; } = new();

        public void Report(double value)
        {
            lock (Values)
            {
                Values.Add(value);
            }
        }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}
