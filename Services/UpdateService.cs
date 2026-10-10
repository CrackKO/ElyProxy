using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using ElyProxy.Models;
using ElyProxy.Utils;

namespace ElyProxy.Services;

public sealed class UpdateService : IDisposable
{
    public static readonly Uri ManifestUri = new("https://elysiumhub.org/updates/elyproxy/update.json");

    private const long MaxPackageBytes = 750L * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly string? _updatesDirectory;

    public UpdateService()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
    {
    }

    public UpdateService(HttpClient httpClient, string? updatesDirectory = null)
    {
        _httpClient = httpClient;
        _updatesDirectory = updatesDirectory;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ElyProxy/{CurrentVersion}");
    }

    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version
        ?? Assembly.GetExecutingAssembly().GetName().Version
        ?? new Version(0, 0, 0);

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(ManifestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var manifest = JsonHelper.Deserialize<UpdateManifest>(json)
            ?? throw new InvalidDataException("Сервер обновлений вернул пустой manifest.");

        ValidateManifest(manifest);
        var version = ParseVersion(manifest.Version);
        return new UpdateCheckResult(manifest, version, version > CurrentVersion);
    }

    public async Task<string> DownloadVerifiedPackageAsync(
        UpdateManifest manifest,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateManifest(manifest);
        var version = ParseVersion(manifest.Version);
        var destinationDirectory = _updatesDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ElyProxy",
            "updates");
        Directory.CreateDirectory(destinationDirectory);

        var finalPath = Path.Combine(destinationDirectory, $"ElyProxy-{version}-win-x64.zip");
        var partialPath = finalPath + ".partial";
        DeleteIfExists(partialPath);

        try
        {
            using var response = await _httpClient.GetAsync(
                new Uri(manifest.PackageUrl),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is > MaxPackageBytes)
                throw new InvalidDataException("Пакет обновления превышает допустимый размер.");

            var total = response.Content.Headers.ContentLength;
            var buffer = new byte[81920];
            long downloaded = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    downloaded += read;
                    if (downloaded > MaxPackageBytes)
                        throw new InvalidDataException("Пакет обновления превышает допустимый размер.");

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    if (total is > 0)
                        progress?.Report(downloaded * 100d / total.Value);
                }

                await destination.FlushAsync(cancellationToken);
            }

            // FileShare.None above intentionally prevents another process from
            // reading a half-written archive. Dispose it before hashing.
            var actualHash = await ComputeSha256Async(partialPath, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualHash),
                    Convert.FromHexString(manifest.Sha256)))
            {
                throw new InvalidDataException("Контрольная сумма пакета обновления не совпадает.");
            }

            DeleteIfExists(finalPath);
            File.Move(partialPath, finalPath);
            progress?.Report(100);
            return finalPath;
        }
        catch
        {
            DeleteIfExists(partialPath);
            throw;
        }
    }

    public static void LaunchUpdater(string packagePath)
    {
        var appDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var updaterDirectory = Path.Combine(appDirectory, "updater");
        var updaterPath = Path.Combine(updaterDirectory, "ElyProxy.Updater.exe");
        if (!File.Exists(updaterPath))
            throw new FileNotFoundException("Компонент обновления ElyProxy.Updater.exe не найден.", updaterPath);

        if (!File.Exists(packagePath))
            throw new FileNotFoundException("Скачанный пакет обновления не найден.", packagePath);

        var temporaryUpdaterDirectory = Path.Combine(Path.GetTempPath(), $"ElyProxy.Updater-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryUpdaterDirectory);
        foreach (var sourceFile in Directory.EnumerateFiles(updaterDirectory))
        {
            var destinationFile = Path.Combine(temporaryUpdaterDirectory, Path.GetFileName(sourceFile));
            File.Copy(sourceFile, destinationFile, overwrite: false);
        }

        var temporaryUpdater = Path.Combine(temporaryUpdaterDirectory, "ElyProxy.Updater.exe");

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = temporaryUpdater,
                Arguments = $"--install-dir \"{appDirectory}\" --package \"{packagePath}\" --wait-pid {Environment.ProcessId}",
                UseShellExecute = true,
                WorkingDirectory = temporaryUpdaterDirectory
            });
            if (process is null)
                throw new InvalidOperationException("Не удалось запустить компонент обновления.");
        }
        catch
        {
            if (Directory.Exists(temporaryUpdaterDirectory))
                Directory.Delete(temporaryUpdaterDirectory, recursive: true);
            throw;
        }
    }

    private static void ValidateManifest(UpdateManifest manifest)
    {
        _ = ParseVersion(manifest.Version);
        if (string.IsNullOrWhiteSpace(manifest.ReleaseNotes))
            manifest.ReleaseNotes = "Описание изменений не предоставлено.";

        if (!Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out var packageUri)
            || packageUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(packageUri.Host, ManifestUri.Host, StringComparison.OrdinalIgnoreCase)
            || !packageUri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Manifest содержит недопустимый адрес пакета обновления.");
        }

        if (manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Manifest содержит недопустимую SHA-256 сумму.");
    }

    private static Version ParseVersion(string source)
    {
        if (!Version.TryParse(source, out var version) || version is null || version < new Version(1, 0))
            throw new InvalidDataException("Manifest содержит недопустимую версию.");

        return version;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed record UpdateCheckResult(UpdateManifest Manifest, Version Version, bool IsAvailable);
