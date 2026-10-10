using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ElyProxy.Core;
using ElyProxy.Models;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Services;

public sealed class CoreUpdateService : IDisposable
{
	private sealed record CoreCatalogEntry(string CoreId, string DisplayName, string Repository, string[] RequiredFiles, Func<string, bool> MatchesAsset, string? CanonicalFileName);

	private static readonly CoreCatalogEntry[] Catalog = new CoreCatalogEntry[]
	{
		new("xray", "Xray-core", "XTLS/Xray-core", new string[4] { "xray.exe", "geoip.dat", "geosite.dat", "wintun.dll" }, (string name) => name.Equals("Xray-windows-64.zip", StringComparison.OrdinalIgnoreCase), null),
		new("sing-box", "sing-box", "SagerNet/sing-box", new string[2] { "sing-box.exe", "libcronet.dll" }, (string name) => name.EndsWith("-windows-amd64.zip", StringComparison.OrdinalIgnoreCase), null),
		new("tun2proxy", "tun2proxy", "tun2proxy/tun2proxy", new string[2] { "tun2proxy-bin.exe", "wintun.dll" }, (string name) => name.Equals("tun2proxy-x86_64-pc-windows-msvc.zip", StringComparison.OrdinalIgnoreCase), null),
		new("hysteria", "Hysteria 2", "HyNetworks/hysteria", new string[1] { "hysteria.exe" }, (string name) => name.Equals("hysteria-windows-amd64.exe", StringComparison.OrdinalIgnoreCase), "hysteria.exe")
	};

	private const long MaxAssetBytes = 250L * 1024 * 1024;

	private readonly HttpClient _httpClient;
	private readonly string _coresRoot;
	private readonly Func<string, CancellationToken, Task<string>> _versionProbe;

	public CoreUpdateService()
		: this(new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
	{
	}

	public CoreUpdateService(
		HttpClient httpClient,
		string? coresRoot = null,
		Func<string, CancellationToken, Task<string>>? versionProbe = null)
	{
		_httpClient = httpClient;
		_coresRoot = coresRoot ?? CoreLocator.CoresRoot;
		_versionProbe = versionProbe ?? ((coreId, token) => CoreVersionService.GetInstalledVersionAsync(coreId, token));
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ElyProxy-CoreUpdater/1.0");
		_httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
	}

	public static IReadOnlyList<string> CoreIds => Catalog.Select(entry => entry.CoreId).ToArray();

	public static string GetDisplayName(string coreId)
	{
		return Catalog.FirstOrDefault(entry => entry.CoreId == coreId)?.DisplayName ?? coreId;
	}

	public async Task<CoreRelease> GetLatestAsync(string coreId, CancellationToken cancellationToken = default)
	{
		CoreCatalogEntry entry = Catalog.FirstOrDefault(candidate => candidate.CoreId == coreId)
			?? throw new ArgumentException("Неизвестное ядро: " + coreId, nameof(coreId));
		using HttpResponseMessage response = await _httpClient.GetAsync(
			$"https://api.github.com/repos/{entry.Repository}/releases/latest",
			cancellationToken);
		response.EnsureSuccessStatusCode();
		string json = await response.Content.ReadAsStringAsync(cancellationToken);
		JObject release = JObject.Parse(json);
		string tag = (string?)release["tag_name"] ?? throw new InvalidDataException("GitHub не вернул tag_name для " + entry.DisplayName + ".");
		JToken? asset = release["assets"]?.FirstOrDefault(candidate => entry.MatchesAsset((string?)candidate["name"] ?? string.Empty));
		if (asset == null)
		{
			throw new InvalidDataException("В релизе " + entry.DisplayName + " " + tag + " не найден windows-x64 файл.");
		}
		string assetName = (string?)asset["name"] ?? string.Empty;
		string assetUrl = (string?)asset["browser_download_url"] ?? string.Empty;
		ValidateAssetUrl(assetUrl);
		string digest = NormalizeDigest((string?)asset["digest"]);
		string version = ExtractVersion(tag);
		bool isArchive = assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
		return new CoreRelease(entry.CoreId, entry.DisplayName, version, assetName, assetUrl, digest, isArchive);
	}

	public async Task<string> InstallAsync(CoreRelease release, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
	{
		CoreCatalogEntry entry = Catalog.FirstOrDefault(candidate => candidate.CoreId == release.CoreId)
			?? throw new ArgumentException("Неизвестное ядро: " + release.CoreId, nameof(release.CoreId));
		ValidateAssetUrl(release.AssetUrl);
		string installDirectory = Path.Combine(_coresRoot, entry.CoreId);
		Directory.CreateDirectory(installDirectory);
		string downloadPath = Path.Combine(installDirectory, release.AssetName + ".download");
		DeleteIfExists(downloadPath);
		List<(string Target, string Backup)> backups = new List<(string, string)>();
		List<string> createdTargets = new List<string>();

		try
		{
			await DownloadAsync(release.AssetUrl, downloadPath, progress, cancellationToken);
			if (!string.IsNullOrEmpty(release.Sha256))
			{
				string actualHash = await ComputeSha256Async(downloadPath, cancellationToken);
				if (!CryptographicOperations.FixedTimeEquals(
						Convert.FromHexString(actualHash),
						Convert.FromHexString(release.Sha256)))
				{
					throw new InvalidDataException("Контрольная сумма ядра " + release.DisplayName + " не совпадает.");
				}
			}

			foreach (string fileName in entry.RequiredFiles)
			{
				string target = Path.Combine(installDirectory, fileName);
				if (File.Exists(target))
				{
					string backup = target + ".bak";
					DeleteIfExists(backup);
					File.Copy(target, backup, overwrite: true);
					backups.Add((target, backup));
				}
				else
				{
					createdTargets.Add(target);
				}
			}

			if (release.IsArchive)
			{
				InstallFromArchive(downloadPath, installDirectory, entry);
			}
			else
			{
				string targetName = entry.CanonicalFileName ?? release.AssetName;
				string targetPath = Path.Combine(installDirectory, targetName);
				DeleteIfExists(targetPath);
				File.Move(downloadPath, targetPath);
			}
			progress?.Report(100);

			string installedVersion = await _versionProbe(entry.CoreId, cancellationToken);
			if (installedVersion is "неизвестно" or "не установлено")
			{
				throw new InvalidDataException($"Ядро {entry.DisplayName} не запускается после установки. Выполнен откат к предыдущей версии.");
			}

			foreach ((string _, string backup) in backups)
			{
				DeleteIfExists(backup);
			}
			return installDirectory;
		}
		catch
		{
			foreach ((string target, string backup) in backups)
			{
				try
				{
					if (File.Exists(backup))
					{
						DeleteIfExists(target);
						File.Move(backup, target);
					}
				}
				catch
				{
				}
			}
			foreach (string target in createdTargets)
			{
				try
				{
					DeleteIfExists(target);
				}
				catch
				{
				}
			}
			throw;
		}
		finally
		{
			DeleteIfExists(downloadPath);
		}
	}

	private async Task DownloadAsync(string url, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken)
	{
		using HttpResponseMessage response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();
		if (response.Content.Headers.ContentLength is > MaxAssetBytes)
		{
			throw new InvalidDataException("Файл ядра превышает допустимый размер.");
		}
		long total = response.Content.Headers.ContentLength ?? 0;
		long downloaded = 0;
		byte[] buffer = new byte[81920];
		await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
		await using FileStream destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
		int read;
		while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
		{
			downloaded += read;
			if (downloaded > MaxAssetBytes)
			{
				throw new InvalidDataException("Файл ядра превышает допустимый размер.");
			}
			await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
			if (total > 0)
			{
				progress?.Report(downloaded * 100d / total);
			}
		}
		await destination.FlushAsync(cancellationToken);
	}

	private void InstallFromArchive(string archivePath, string installDirectory, CoreCatalogEntry entry)
	{
		string extractDirectory = Path.Combine(_coresRoot, entry.CoreId + ".extract-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(extractDirectory);
		try
		{
			ZipFile.ExtractToDirectory(archivePath, extractDirectory);
			foreach (string requiredFile in entry.RequiredFiles)
			{
				string? source = Directory.EnumerateFiles(extractDirectory, requiredFile, SearchOption.AllDirectories).FirstOrDefault();
				if (source == null)
				{
					if (requiredFile is "wintun.dll" or "libcronet.dll")
					{
						continue;
					}
					throw new InvalidDataException("В архиве " + entry.DisplayName + " не найден " + requiredFile + ".");
				}
				string target = Path.Combine(installDirectory, requiredFile);
				DeleteIfExists(target);
				File.Copy(source, target, overwrite: true);
			}
		}
		finally
		{
			try
			{
				if (Directory.Exists(extractDirectory))
				{
					Directory.Delete(extractDirectory, recursive: true);
				}
			}
			catch
			{
			}
		}
	}

	private static void ValidateAssetUrl(string url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
			|| uri.Scheme != Uri.UriSchemeHttps
			|| (uri.Host != "github.com" && uri.Host != "objects.githubusercontent.com"))
		{
			throw new InvalidDataException("Недопустимый адрес загрузки ядра.");
		}
	}

	private static string NormalizeDigest(string? digest)
	{
		if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
		{
			return string.Empty;
		}
		string hash = digest.Substring("sha256:".Length).Trim();
		if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
		{
			return string.Empty;
		}
		return hash.ToUpperInvariant();
	}

	public static string ExtractVersion(string tag)
	{
		Match match = Regex.Match(tag ?? string.Empty, @"\d+\.\d+\.\d+");
		return (match.Success ? match.Value : (tag ?? string.Empty).TrimStart('v'));
	}

	private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
	{
		await using FileStream stream = File.OpenRead(path);
		byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
		return Convert.ToHexString(hash);
	}

	private static void DeleteIfExists(string path)
	{
		if (File.Exists(path))
		{
			File.Delete(path);
		}
	}

	public void Dispose()
	{
		_httpClient.Dispose();
		GC.SuppressFinalize(this);
	}
}
