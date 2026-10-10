using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ElyProxy.Core;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Services;

/// <summary>
/// Updates Xray geoip.dat/geosite.dat from the community-maintained
/// Loyalsoldier/v2ray-rules-dat releases (richer RU categories than the
/// Xray-bundled files). Files are installed next to the active xray.exe.
/// </summary>
public sealed class GeoDataUpdateService : IDisposable
{
	private const string ReleasesUrl = "https://api.github.com/repos/Loyalsoldier/v2ray-rules-dat/releases/latest";

	private static readonly string[] AssetNames = new string[2] { "geoip.dat", "geosite.dat" };

	private const long MaxAssetBytes = 150L * 1024 * 1024;

	private readonly HttpClient _httpClient;
	private readonly string? _targetDirectoryOverride;

	public GeoDataUpdateService()
		: this(new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
	{
	}

	public GeoDataUpdateService(HttpClient httpClient, string? targetDirectoryOverride = null)
	{
		_httpClient = httpClient;
		_targetDirectoryOverride = targetDirectoryOverride;
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ElyProxy-GeoUpdater/1.0");
		_httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
	}

	public async Task<string> UpdateXrayGeoAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
	{
		string targetDirectory;
		if (_targetDirectoryOverride != null)
		{
			targetDirectory = _targetDirectoryOverride;
		}
		else
		{
			string xrayPath = CoreLocator.Resolve("xray", "xray.exe", "xray", Path.Combine("bin", "xray"), string.Empty);
			targetDirectory = Path.GetDirectoryName(xrayPath) ?? string.Empty;
			if (targetDirectory.Length == 0 || !File.Exists(xrayPath))
			{
				throw new FileNotFoundException("xray.exe не найден — сначала обновите ядро Xray в разделе «Ядра».", xrayPath);
			}
		}

		using HttpResponseMessage releaseResponse = await _httpClient.GetAsync(ReleasesUrl, cancellationToken);
		releaseResponse.EnsureSuccessStatusCode();
		JObject release = JObject.Parse(await releaseResponse.Content.ReadAsStringAsync(cancellationToken));
		JArray assets = ((JArray?)release["assets"]) ?? new JArray();
		List<(string Name, string Url, string Sha256)> toDownload = new List<(string, string, string)>();
		foreach (string assetName in AssetNames)
		{
			JToken? asset = assets.FirstOrDefault(candidate => string.Equals((string?)candidate["name"], assetName, StringComparison.OrdinalIgnoreCase));
			if (asset == null)
			{
				throw new InvalidDataException("В релизе geo-данных не найден файл " + assetName + ".");
			}
			string url = ((string?)asset["browser_download_url"] ?? string.Empty);
			ValidateAssetUrl(url);
			toDownload.Add((assetName, url, NormalizeDigest((string?)asset["digest"])));
		}

		string tempDirectory = Path.Combine(Path.GetTempPath(), "ElyProxy", "geo-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempDirectory);
		List<(string Target, string Backup)> backups = new List<(string, string)>();
		List<string> installedFiles = new List<string>();
		try
		{
			double reported = 0;
			foreach ((string name, string url, string sha256) in toDownload)
			{
				string downloadedPath = Path.Combine(tempDirectory, name);
				await DownloadAsync(url, downloadedPath, cancellationToken);
				if (!string.IsNullOrEmpty(sha256))
				{
					string actualHash = await ComputeSha256Async(downloadedPath, cancellationToken);
					if (!CryptographicOperations.FixedTimeEquals(
							Convert.FromHexString(actualHash),
							Convert.FromHexString(sha256)))
					{
						throw new InvalidDataException("Контрольная сумма " + name + " не совпадает.");
					}
				}
				progress?.Report(reported += 50);
			}

			foreach ((string name, string _, string _) in toDownload)
			{
				string sourcePath = Path.Combine(tempDirectory, name);
				string targetPath = Path.Combine(targetDirectory, name);
				if (File.Exists(targetPath))
				{
					string backupPath = targetPath + ".bak";
					if (File.Exists(backupPath))
					{
						File.Delete(backupPath);
					}
					File.Copy(targetPath, backupPath, overwrite: true);
					backups.Add((targetPath, backupPath));
				}
				File.Copy(sourcePath, targetPath, overwrite: true);
				installedFiles.Add(targetPath);
			}

			foreach ((string _, string backupPath) in backups)
			{
				try
				{
					if (File.Exists(backupPath))
					{
						File.Delete(backupPath);
					}
				}
				catch
				{
				}
			}

			progress?.Report(100);
			string sizes = string.Join(", ", installedFiles.Select(path => Path.GetFileName(path) + " " + (new FileInfo(path).Length / 1024 / 1024) + " МБ"));
			return "Geo-данные обновлены в " + targetDirectory + ": " + sizes;
		}
		catch
		{
			foreach ((string targetPath, string backupPath) in backups)
			{
				try
				{
					if (File.Exists(backupPath))
					{
						File.Copy(backupPath, targetPath, overwrite: true);
					}
				}
				catch
				{
				}
			}
			throw;
		}
		finally
		{
			try
			{
				if (Directory.Exists(tempDirectory))
				{
					Directory.Delete(tempDirectory, recursive: true);
				}
			}
			catch
			{
			}
		}
	}

	private async Task DownloadAsync(string url, string destinationPath, CancellationToken cancellationToken)
	{
		using HttpResponseMessage response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();
		if (response.Content.Headers.ContentLength is > MaxAssetBytes)
		{
			throw new InvalidDataException("Файл geo-данных превышает допустимый размер.");
		}
		await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
		await using FileStream destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
		await source.CopyToAsync(destination, 81920, cancellationToken);
	}

	private static void ValidateAssetUrl(string url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
			|| uri.Scheme != Uri.UriSchemeHttps
			|| (uri.Host != "github.com" && uri.Host != "objects.githubusercontent.com"))
		{
			throw new InvalidDataException("Недопустимый адрес загрузки geo-данных.");
		}
	}

	private static string NormalizeDigest(string? digest)
	{
		if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
		{
			return string.Empty;
		}
		string hash = digest.Substring("sha256:".Length).Trim();
		return ((hash.Length == 64 && hash.All(Uri.IsHexDigit)) ? hash.ToUpperInvariant() : string.Empty);
	}

	private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
	{
		await using FileStream stream = File.OpenRead(path);
		byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
		return Convert.ToHexString(hash);
	}

	public void Dispose()
	{
		_httpClient.Dispose();
		GC.SuppressFinalize(this);
	}
}
