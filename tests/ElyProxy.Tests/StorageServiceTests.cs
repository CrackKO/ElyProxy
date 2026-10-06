using ElyProxy.Models;
using ElyProxy.Services;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Tests;

public sealed class StorageServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ElyProxy-StorageTests-" + Guid.NewGuid().ToString("N"));
    private readonly StorageService _storage;

    public StorageServiceTests()
    {
        _storage = new StorageService(_directory);
    }

    [Fact]
    public async Task SaveSettingsAsync_WritesVersionedDocumentAndLoadsIt()
    {
        var settings = new AppSettings { SocksPort = 2090, AutoConnect = true };

        await _storage.SaveSettingsAsync(settings);
        StorageLoadResult<AppSettings> result = await _storage.LoadSettingsAsync();

        Assert.Equal(StorageLoadStatus.Loaded, result.Status);
        Assert.Equal(2090, result.Data.SocksPort);
        Assert.True(result.Data.AutoConnect);
        Assert.Equal(1, JObject.Parse(await File.ReadAllTextAsync(Path.Combine(_directory, "settings.json")))["SchemaVersion"]!.Value<int>());
    }

    [Fact]
    public async Task LoadSettingsAsync_MigratesLegacyDocumentAndKeepsBackup()
    {
        string path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, "{\"SocksPort\": 2088, \"AutoConnect\": true}");

        StorageLoadResult<AppSettings> result = await _storage.LoadSettingsAsync();

        Assert.Equal(StorageLoadStatus.Migrated, result.Status);
        Assert.Equal(2088, result.Data.SocksPort);
        Assert.True(result.Data.AutoConnect);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_directory, "backups"), "settings-*.json"));
        Assert.NotNull(JObject.Parse(await File.ReadAllTextAsync(path))["Data"]);
    }

    [Fact]
    public async Task LoadSettingsAsync_RecoversLatestValidBackupAndPreservesCorruptFile()
    {
        await _storage.SaveSettingsAsync(new AppSettings { SocksPort = 2081 });
        await _storage.SaveSettingsAsync(new AppSettings { SocksPort = 2082 });
        string path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, "{ invalid json");

        StorageLoadResult<AppSettings> result = await _storage.LoadSettingsAsync();

        Assert.Equal(StorageLoadStatus.RecoveredFromBackup, result.Status);
        Assert.Equal(2081, result.Data.SocksPort);
        Assert.Single(Directory.EnumerateFiles(_directory, "settings.json.corrupt-*.json"));
        Assert.Equal(2081, (await _storage.LoadSettingsAsync()).Data.SocksPort);
    }

    [Fact]
    public async Task LoadSettingsAsync_WithoutBackupBlocksOverwriteAndRetainsOriginal()
    {
        string path = Path.Combine(_directory, "settings.json");
        const string damaged = "{ damaged settings";
        await File.WriteAllTextAsync(path, damaged);

        StorageLoadResult<AppSettings> result = await _storage.LoadSettingsAsync();

        Assert.Equal(StorageLoadStatus.Unavailable, result.Status);
        Assert.False(result.CanSave);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _storage.SaveSettingsAsync(new AppSettings()));
        Assert.Equal(damaged, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task LoadSettingsAsync_WhenMainAndAllBackupsAreUnavailableBlocksOverwrite()
    {
        await _storage.SaveSettingsAsync(new AppSettings { SocksPort = 2081 });
        await _storage.SaveSettingsAsync(new AppSettings { SocksPort = 2082 });
        string path = Path.Combine(_directory, "settings.json");
        File.Delete(path);
        foreach (string backup in Directory.EnumerateFiles(Path.Combine(_directory, "backups"), "settings-*.json"))
        {
            await File.WriteAllTextAsync(backup, "{ damaged backup");
        }

        StorageLoadResult<AppSettings> result = await _storage.LoadSettingsAsync();

        Assert.Equal(StorageLoadStatus.Unavailable, result.Status);
        Assert.False(result.CanSave);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _storage.SaveSettingsAsync(new AppSettings()));
    }

    [Fact]
    public async Task SaveSettingsAsync_RetainsOnlyThreeBackups()
    {
        for (int index = 0; index < 6; index++)
        {
            await _storage.SaveSettingsAsync(new AppSettings { SocksPort = 3000 + index });
            await Task.Delay(2);
        }

        Assert.Equal(3, Directory.EnumerateFiles(Path.Combine(_directory, "backups"), "settings-*.json").Count());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
