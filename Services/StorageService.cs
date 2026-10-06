using System.Text;
using ElyProxy.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Services;

public sealed class StorageService
{
    private const int CurrentSchemaVersion = 1;
    private const int BackupRetentionCount = 3;
    private readonly string _basePath;
    private readonly string _backupPath;
    private readonly HashSet<string> _writeBlockedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string DataDirectory => _basePath;

    public StorageService(string? basePath = null)
    {
        _basePath = basePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ElyProxy");
        _backupPath = Path.Combine(_basePath, "backups");
        Directory.CreateDirectory(_basePath);
    }

    public Task<StorageLoadResult<List<Subscription>>> LoadSubscriptionsAsync() =>
        LoadAsync("subscriptions.json", static () => new List<Subscription>());

    public Task SaveSubscriptionsAsync(IEnumerable<Subscription> subscriptions) =>
        SaveAsync("subscriptions.json", subscriptions.ToList());

    public Task<StorageLoadResult<List<ProxyProfile>>> LoadProfilesAsync() =>
        LoadAsync("profiles.json", static () => new List<ProxyProfile>());

    public Task SaveProfilesAsync(IEnumerable<ProxyProfile> profiles) =>
        SaveAsync("profiles.json", profiles.ToList());

    public Task<StorageLoadResult<AppSettings>> LoadSettingsAsync() =>
        LoadAsync("settings.json", static () => new AppSettings());

    public Task SaveSettingsAsync(AppSettings settings) =>
        SaveAsync("settings.json", settings);

    private async Task<StorageLoadResult<T>> LoadAsync<T>(string fileName, Func<T> createDefault)
    {
        string path = Path.Combine(_basePath, fileName);
        if (!File.Exists(path))
        {
            StorageLoadResult<T>? backupResult = await TryRecoverFromBackupAsync<T>(path);
            if (backupResult != null)
            {
                return backupResult;
            }

            string backupPrefix = Path.GetFileNameWithoutExtension(path) + "-";
            bool hasUnreadableBackups = Directory.Exists(_backupPath)
                && Directory.EnumerateFiles(_backupPath, backupPrefix + "*.json").Any();
            if (hasUnreadableBackups)
            {
                _writeBlockedPaths.Add(path);
                return new StorageLoadResult<T>(
                    createDefault(),
                    StorageLoadStatus.Unavailable,
                    $"Файл {Path.GetFileName(path)} отсутствует, а резервные копии не удалось прочитать. Запись заблокирована, чтобы не затереть единственную сохранившуюся копию.");
            }

            return new StorageLoadResult<T>(createDefault(), StorageLoadStatus.Missing);
        }

        try
        {
            var (data, migrated) = await ReadDocumentAsync<T>(path);
            if (migrated)
            {
                await SaveAsync(fileName, data, createBackup: true);
                return new StorageLoadResult<T>(data, StorageLoadStatus.Migrated);
            }

            return new StorageLoadResult<T>(data, StorageLoadStatus.Loaded);
        }
        catch (Exception ex)
        {
            StorageLoadResult<T>? backupResult = await TryRecoverFromBackupAsync<T>(path);
            if (backupResult != null)
            {
                return backupResult with { ErrorMessage = $"Основной файл был повреждён; восстановлена локальная копия. {ex.Message}" };
            }

            _writeBlockedPaths.Add(path);
            return new StorageLoadResult<T>(
                createDefault(),
                StorageLoadStatus.Unavailable,
                $"Не удалось прочитать {Path.GetFileName(path)}. Исходный файл сохранён, запись поверх него заблокирована. {ex.Message}");
        }
    }

    private async Task<StorageLoadResult<T>?> TryRecoverFromBackupAsync<T>(string path)
    {
        if (!Directory.Exists(_backupPath))
        {
            return null;
        }

        string prefix = Path.GetFileNameWithoutExtension(path) + "-";
        foreach (string backupFile in Directory.EnumerateFiles(_backupPath, prefix + "*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var (data, _) = await ReadDocumentAsync<T>(backupFile);
                if (File.Exists(path))
                {
                    string preservedPath = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".json";
                    File.Copy(path, preservedPath, overwrite: false);
                }

                await SaveAsync(Path.GetFileName(path), data, createBackup: false);
                _writeBlockedPaths.Remove(path);
                return new StorageLoadResult<T>(data, StorageLoadStatus.RecoveredFromBackup);
            }
            catch
            {
                // A damaged backup is skipped; the original file remains untouched.
            }
        }

        return null;
    }

    private async Task SaveAsync<T>(string fileName, T data, bool createBackup = true)
    {
        await _writeLock.WaitAsync();
        try
        {
            string path = Path.Combine(_basePath, fileName);
            if (_writeBlockedPaths.Contains(path))
            {
                throw new InvalidOperationException(
                    $"Запись в {Path.GetFileName(path)} заблокирована: файл не удалось восстановить. Исходные данные сохранены.");
            }

            Directory.CreateDirectory(_basePath);
            if (createBackup && File.Exists(path))
            {
                await CreateBackupAsync(path);
            }

            string json = JsonConvert.SerializeObject(
                new StorageDocument<T> { SchemaVersion = CurrentSchemaVersion, Data = data },
                Formatting.Indented,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            await WriteAtomicAsync(path, json);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task CreateBackupAsync(string path)
    {
        Directory.CreateDirectory(_backupPath);
        string prefix = Path.GetFileNameWithoutExtension(path);
        string backupFile = Path.Combine(
            _backupPath,
            $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json");
        File.Copy(path, backupFile, overwrite: false);

        string[] backups = Directory.EnumerateFiles(_backupPath, prefix + "-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        foreach (string staleBackup in backups.Skip(BackupRetentionCount))
        {
            File.Delete(staleBackup);
        }
    }

    private static async Task<(T Data, bool Migrated)> ReadDocumentAsync<T>(string path)
    {
        string json = await File.ReadAllTextAsync(path);
        JToken root = JToken.Parse(json);
        JToken? schemaToken = root.Type == JTokenType.Object
            ? ((JObject)root).GetValue("schemaVersion", StringComparison.OrdinalIgnoreCase)
            : null;
        JToken? dataToken = root.Type == JTokenType.Object
            ? ((JObject)root).GetValue("data", StringComparison.OrdinalIgnoreCase)
            : null;

        if (schemaToken != null && dataToken != null)
        {
            int schemaVersion = schemaToken.Value<int>();
            if (schemaVersion > CurrentSchemaVersion || schemaVersion < 0)
            {
                throw new InvalidDataException($"Неподдерживаемая версия схемы хранилища: {schemaVersion}.");
            }

            T? envelopeData = dataToken.ToObject<T>();
            if (envelopeData == null)
            {
                throw new InvalidDataException("В хранилище отсутствуют данные.");
            }

            return (envelopeData, schemaVersion < CurrentSchemaVersion);
        }

        T? legacyData = root.ToObject<T>();
        if (legacyData == null)
        {
            throw new InvalidDataException("Файл не содержит данных ожидаемого типа.");
        }

        return (legacyData, true);
    }

    private static async Task WriteAtomicAsync(string path, string content)
    {
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, null);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed class StorageDocument<T>
    {
        public int SchemaVersion { get; set; }

        public T? Data { get; set; }
    }
}
