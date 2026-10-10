using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using ElyProxy.Models;
using ElyProxy.Services;
using ElyProxy.Utils;

namespace ElyProxy.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ElyProxy-UpdateTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CheckAsync_AcceptsNewerManifestAndKeepsReleaseNotes()
    {
        using var service = CreateService(Manifest(version: "99.0.0", notes: "Тестовые заметки"));

        var result = await service.CheckAsync();

        Assert.True(result.IsAvailable);
        Assert.Equal(new Version(99, 0, 0), result.Version);
        Assert.Equal("Тестовые заметки", result.Manifest.ReleaseNotes);
    }

    [Fact]
    public async Task CheckAsync_ReportsCurrentVersionAsUpToDate()
    {
        using var service = CreateService(Manifest(version: "1.0.0"));

        var result = await service.CheckAsync();

        Assert.False(result.IsAvailable);
    }

    [Fact]
    public async Task CheckAsync_DefaultsEmptyReleaseNotes()
    {
        using var service = CreateService(Manifest(version: "99.0.0", notes: ""));

        var result = await service.CheckAsync();

        Assert.Equal("Описание изменений не предоставлено.", result.Manifest.ReleaseNotes);
    }

    [Theory]
    [InlineData("0.9", null, null)]
    [InlineData("abc", null, null)]
    [InlineData("99.0.0", "http://elysiumhub.org/updates/elyproxy/ElyProxy-99.0.0-win-x64.zip", null)]
    [InlineData("99.0.0", "https://evil.example/ElyProxy-99.0.0-win-x64.zip", null)]
    [InlineData("99.0.0", "https://elysiumhub.org.evil.example/ElyProxy-99.0.0-win-x64.zip", null)]
    [InlineData("99.0.0", "https://elysiumhub.org/updates/elyproxy/ElyProxy-99.0.0-win-x64.tar.gz", null)]
    [InlineData("99.0.0", null, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("99.0.0", null, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("99.0.0", null, "gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    [InlineData("99.0.0", null, "")]
    public async Task CheckAsync_RejectsInvalidManifests(string version, string? packageUrl, string? sha256)
    {
        using var service = CreateService(Manifest(version, packageUrl, sha256));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync());
    }

    [Fact]
    public async Task DownloadVerifiedPackageAsync_WritesPackageAndVerifiesHash()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var sha = Convert.ToHexString(SHA256.HashData(payload));
        var progress = new RecordingProgress();
        using var service = CreateService(Manifest(version: "99.0.0", sha256: sha), payload);

        var path = await service.DownloadVerifiedPackageAsync(Manifest(version: "99.0.0", sha256: sha), progress);

        Assert.StartsWith(Path.GetFullPath(_directory), Path.GetFullPath(path));
        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".partial"));
        Assert.Equal(100d, progress.Values[^1]);
    }

    [Fact]
    public async Task DownloadVerifiedPackageAsync_HashMismatch_RemovesPartialFile()
    {
        var payload = new byte[] { 9, 9, 9 };
        using var service = CreateService(Manifest(version: "99.0.0"), payload);
        var manifest = Manifest(version: "99.0.0");

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadVerifiedPackageAsync(manifest));

        Assert.Empty(Directory.Exists(_directory) ? Directory.GetFiles(_directory) : Array.Empty<string>());
    }

    [Fact]
    public async Task DownloadVerifiedPackageAsync_ContentLengthOverLimit_IsRejected()
    {
        using var service = CreateService(Manifest(version: "99.0.0"), new byte[] { 1 }, contentLengthOverride: 800L * 1024 * 1024);
        var manifest = Manifest(version: "99.0.0");

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadVerifiedPackageAsync(manifest));

        Assert.Empty(Directory.Exists(_directory) ? Directory.GetFiles(_directory) : Array.Empty<string>());
    }

    [Fact]
    public async Task DownloadVerifiedPackageAsync_HttpError_RemovesPartialFile()
    {
        using var service = CreateService(Manifest(version: "99.0.0"), payload: null, packageStatus: HttpStatusCode.ServiceUnavailable);
        var manifest = Manifest(version: "99.0.0");

        await Assert.ThrowsAsync<HttpRequestException>(() => service.DownloadVerifiedPackageAsync(manifest));

        Assert.Empty(Directory.Exists(_directory) ? Directory.GetFiles(_directory) : Array.Empty<string>());
    }

    private UpdateService CreateService(
        UpdateManifest manifest,
        byte[]? payload = null,
        HttpStatusCode packageStatus = HttpStatusCode.OK,
        long? contentLengthOverride = null)
    {
        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri == UpdateService.ManifestUri)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonHelper.Serialize(manifest), System.Text.Encoding.UTF8, "application/json")
                };
            }

            var response = new HttpResponseMessage(packageStatus);
            if (payload != null)
            {
                response.Content = new ByteArrayContent(payload);
                if (contentLengthOverride.HasValue)
                {
                    response.Content.Headers.ContentLength = contentLengthOverride.Value;
                }
            }

            return response;
        });

        return new UpdateService(new HttpClient(handler), _directory);
    }

    private static UpdateManifest Manifest(string version = "99.0.0", string? packageUrl = null, string? sha256 = null, string notes = "Тест")
    {
        return new UpdateManifest
        {
            Version = version,
            PackageUrl = packageUrl ?? "https://elysiumhub.org/updates/elyproxy/ElyProxy-99.0.0-win-x64.zip",
            Sha256 = sha256 ?? new string('a', 64),
            ReleaseNotes = notes
        };
    }

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
