using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using ElyProxy.Models;
using ElyProxy.Services;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Tests;

public sealed class CoreUpdateServiceInstallTests : IDisposable
{
    private const string XrayAssetUrl = "https://github.com/XTLS/Xray-core/releases/download/v26.9.30/Xray-windows-64.zip";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ElyProxy-CoreUpdateTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetLatestAsync_ParsesReleaseAndNormalizesDigest()
    {
        var digest = "sha256:" + new string('a', 64);
        using var service = CreateService(ReleaseJson("v26.9.30", ("Xray-windows-64.zip", XrayAssetUrl, digest)));

        var release = await service.GetLatestAsync("xray");

        Assert.Equal("xray", release.CoreId);
        Assert.Equal("26.9.30", release.Version);
        Assert.Equal("Xray-windows-64.zip", release.AssetName);
        Assert.Equal(XrayAssetUrl, release.AssetUrl);
        Assert.Equal(new string('A', 64), release.Sha256);
        Assert.True(release.IsArchive);
    }

    [Theory]
    [InlineData("sha256:zzz")]
    [InlineData("md5:aaaa")]
    [InlineData(null)]
    public async Task GetLatestAsync_InvalidDigestIsIgnored(string? digest)
    {
        using var service = CreateService(ReleaseJson("v26.9.30", ("Xray-windows-64.zip", XrayAssetUrl, digest)));

        var release = await service.GetLatestAsync("xray");

        Assert.Equal(string.Empty, release.Sha256);
    }

    [Fact]
    public async Task GetLatestAsync_WithoutMatchingAsset_Throws()
    {
        using var service = CreateService(ReleaseJson("v26.9.30", ("other.zip", XrayAssetUrl, null)));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetLatestAsync("xray"));
    }

    [Fact]
    public async Task GetLatestAsync_WithInvalidAssetUrl_Throws()
    {
        using var service = CreateService(ReleaseJson("v26.9.30", ("Xray-windows-64.zip", "http://github.com/x.zip", null)));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetLatestAsync("xray"));
    }

    [Fact]
    public async Task GetLatestAsync_WithoutTagName_Throws()
    {
        using var service = CreateService(ReleaseJson(null, ("Xray-windows-64.zip", XrayAssetUrl, null)));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetLatestAsync("xray"));
    }

    [Fact]
    public async Task GetLatestAsync_UnknownCore_Throws()
    {
        using var service = CreateService(ReleaseJson("v1.0.0"));

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetLatestAsync("unknown-core"));
    }

    [Fact]
    public async Task InstallAsync_InstallsArchiveAndCleansBackups()
    {
        var payload = ZipBytes(("xray.exe", "new-xray"), ("geoip.dat", "new-geoip"), ("geosite.dat", "new-geosite"));
        using var service = CreateInstallService(payload, "26.9.30");
        var release = XrayRelease(payload);

        var installDirectory = await service.InstallAsync(release);

        Assert.Equal(Path.Combine(_directory, "cores", "xray"), installDirectory);
        Assert.Equal("new-xray", File.ReadAllText(Path.Combine(installDirectory, "xray.exe")));
        Assert.Equal("new-geoip", File.ReadAllText(Path.Combine(installDirectory, "geoip.dat")));
        Assert.Equal("new-geosite", File.ReadAllText(Path.Combine(installDirectory, "geosite.dat")));
        Assert.Equal(3, Directory.GetFiles(installDirectory).Length);
        Assert.Empty(Directory.GetFiles(installDirectory, "*.bak"));
        Assert.Empty(Directory.GetFiles(installDirectory, "*.download"));
    }

    [Fact]
    public async Task InstallAsync_RollsBackWhenInstalledCoreDoesNotRun()
    {
        var installDirectory = PrepareInstallDirectory(("xray.exe", "old-xray"), ("geoip.dat", "old-geoip"));
        var payload = ZipBytes(("xray.exe", "new-xray"), ("geoip.dat", "new-geoip"), ("geosite.dat", "new-geosite"));
        using var service = CreateInstallService(payload, "не установлено");

        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(XrayRelease(payload)));

        Assert.Equal("old-xray", File.ReadAllText(Path.Combine(installDirectory, "xray.exe")));
        Assert.Equal("old-geoip", File.ReadAllText(Path.Combine(installDirectory, "geoip.dat")));
        Assert.False(File.Exists(Path.Combine(installDirectory, "geosite.dat")));
        Assert.Empty(Directory.GetFiles(installDirectory, "*.bak"));
        Assert.Empty(Directory.GetFiles(installDirectory, "*.download"));
    }

    [Fact]
    public async Task InstallAsync_ShaMismatch_LeavesExistingFilesUntouched()
    {
        var installDirectory = PrepareInstallDirectory(("xray.exe", "old-xray"));
        var payload = ZipBytes(("xray.exe", "new-xray"));
        using var service = CreateInstallService(payload, "26.9.30");
        var release = XrayRelease(payload) with { Sha256 = new string('a', 64) };

        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(release));

        Assert.Equal("old-xray", File.ReadAllText(Path.Combine(installDirectory, "xray.exe")));
        Assert.Single(Directory.GetFiles(installDirectory));
        Assert.Empty(Directory.GetFiles(installDirectory, "*.download"));
    }

    [Fact]
    public async Task InstallAsync_ArchiveMissingRequiredFile_RollsBack()
    {
        var installDirectory = PrepareInstallDirectory(("xray.exe", "old-xray"));
        var payload = ZipBytes(("xray.exe", "new-xray"));
        using var service = CreateInstallService(payload, "26.9.30");

        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(XrayRelease(payload)));

        Assert.Equal("old-xray", File.ReadAllText(Path.Combine(installDirectory, "xray.exe")));
        Assert.Single(Directory.GetFiles(installDirectory));
        Assert.Empty(Directory.GetFiles(installDirectory, "*.bak"));
    }

    private string PrepareInstallDirectory(params (string Name, string Content)[] files)
    {
        var installDirectory = Path.Combine(_directory, "cores", "xray");
        Directory.CreateDirectory(installDirectory);
        foreach (var (name, content) in files)
        {
            File.WriteAllText(Path.Combine(installDirectory, name), content);
        }

        return installDirectory;
    }

    private CoreRelease XrayRelease(byte[] payload) => new(
        "xray",
        "Xray-core",
        "26.9.30",
        "Xray-windows-64.zip",
        XrayAssetUrl,
        Convert.ToHexString(SHA256.HashData(payload)),
        true);

    private CoreUpdateService CreateService(string releaseJson)
    {
        var handler = new FakeHandler(_ => Json(releaseJson));
        return new CoreUpdateService(new HttpClient(handler), Path.Combine(_directory, "cores"));
    }

    private CoreUpdateService CreateInstallService(byte[] payload, string version)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        return new CoreUpdateService(
            new HttpClient(handler),
            Path.Combine(_directory, "cores"),
            (_, _) => Task.FromResult(version));
    }

    private static string ReleaseJson(string? tag, params (string Name, string Url, string? Digest)[] assets)
    {
        var release = new JObject
        {
            ["assets"] = new JArray(assets.Select(asset => new JObject
            {
                ["name"] = asset.Name,
                ["browser_download_url"] = asset.Url,
                ["digest"] = asset.Digest
            }))
        };
        if (tag != null)
        {
            release["tag_name"] = tag;
        }

        return release.ToString();
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static byte[] ZipBytes(params (string Name, string Content)[] entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return memory.ToArray();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
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
