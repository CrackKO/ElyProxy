using ElyProxy.Models;
using ElyProxy.Services;
using Newtonsoft.Json;

namespace ElyProxy.Tests;

public sealed class ImportExportServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ElyProxy-ImportExportTests-" + Guid.NewGuid().ToString("N"));
    private readonly ImportExportService _service = new();

    [Fact]
    public async Task ImportProfileAsync_RoundTripsProfileData()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "profile.elyproxy");
        var source = new ProxyProfile
        {
            Name = "Work",
            Nodes = [new VlessNode { Name = "Server", Address = "example.com", Port = 443, UUID = "11111111-1111-1111-1111-111111111111" }]
        };

        await _service.ExportProfileAsync(source, path);
        ProxyProfile? imported = await _service.ImportProfileAsync(path);

        Assert.NotNull(imported);
        Assert.Equal("Work", imported.Name);
        Assert.Equal("example.com", Assert.Single(imported.Nodes).Address);
    }

    [Fact]
    public async Task ImportProfileAsync_PropagatesInvalidJsonInsteadOfSilentlyReturningEmpty()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "damaged.elyproxy");
        await File.WriteAllTextAsync(path, "{ damaged json");

        await Assert.ThrowsAsync<JsonReaderException>(() => _service.ImportProfileAsync(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
