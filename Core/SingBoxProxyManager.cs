using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElyProxy.Models;

namespace ElyProxy.Core;

public sealed class SingBoxProxyManager : IDisposable
{
	private static readonly TimeSpan StartupProbeDelay = TimeSpan.FromSeconds(1.0);

	private static readonly TimeSpan ConfigCheckTimeout = TimeSpan.FromSeconds(8.0);

	private readonly IProcessManager _processManager;

	private readonly SingBoxConfigBuilder _configBuilder = new SingBoxConfigBuilder();

	private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);

	private string? _configPath;

	private TaskCompletionSource<bool>? _startCompletion;

	private bool _stopping;

	public bool IsRunning => _processManager.IsRunning;

	public string? LastConfigHash { get; private set; }

	public event Action<string>? LogReceived;

	public event Action<bool>? StatusChanged;

	public SingBoxProxyManager(IProcessManager? processManager = null)
	{
		_processManager = processManager ?? new ProcessManagerRouter();
		_processManager.OutputReceived += OnProcessLine;
		_processManager.ErrorReceived += OnProcessLine;
		_processManager.ProcessExited += OnProcessExited;
	}

	public string GetSingBoxPath()
	{
		return CoreLocator.Resolve("sing-box", "sing-box.exe", "sing-box", Path.Combine("bin", "sing-box"), string.Empty);
	}

	public async Task StartAsync(VlessNode node, int proxyPort, XrayTunOptions options)
	{
		await _lifecycleLock.WaitAsync();
		try
		{
			string singBoxPath = GetSingBoxPath();
			if (!File.Exists(singBoxPath))
			{
				throw new FileNotFoundException("Proxy Pro требует sing-box.exe рядом с ElyProxy.exe.", singBoxPath);
			}
			if (options.RoutingPreset == RoutingPreset.RoscomVpnDefault)
			{
				IReadOnlyList<string> missingAssets = RoscomVpnDefaultRouting.GetMissingAssetPaths();
				if (missingAssets.Count > 0)
				{
					throw new FileNotFoundException("Не найдены rule-set'ы RoscomVPN DEFAULT: " + string.Join(", ", missingAssets));
				}
			}
			await StopProcessAsync();
			CleanupConfig();
			string configJson = _configBuilder.BuildSystemProxy(node, proxyPort, options);
			LastConfigHash = ConfigHash.Compute(configJson);
		this.LogReceived?.Invoke("[sys] Хеш конфига: " + LastConfigHash);
			await WriteConfigAsync(configJson);
			await ValidateConfigurationAsync(singBoxPath, _configPath);
			_startCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			string workingDirectory = Path.GetDirectoryName(singBoxPath);
			await _processManager.StartAsync(singBoxPath, "run -c \"" + _configPath + "\"", workingDirectory, stopExisting: false);
			if (await Task.WhenAny(_startCompletion.Task, Task.Delay(StartupProbeDelay)) == _startCompletion.Task)
			{
				await _startCompletion.Task;
			}
			else if (!_processManager.IsRunning)
			{
				throw new InvalidOperationException("sing-box завершился сразу после запуска Proxy Pro.");
			}
			_startCompletion = null;
			this.StatusChanged?.Invoke(obj: true);
			this.LogReceived?.Invoke($"[sys] Proxy Pro: локальный HTTP/SOCKS {proxyPort}, TUN не используется");
		}
		catch
		{
			await StopProcessAsync();
			CleanupConfig();
			this.StatusChanged?.Invoke(obj: false);
			throw;
		}
		finally
		{
			_lifecycleLock.Release();
		}
	}

	public async Task StopAsync()
	{
		await _lifecycleLock.WaitAsync();
		try
		{
			await StopProcessAsync();
			CleanupConfig();
			this.StatusChanged?.Invoke(obj: false);
		}
		finally
		{
			_lifecycleLock.Release();
		}
	}

	private async Task WriteConfigAsync(string configJson)
	{
		string tempDir = Path.Combine(Path.GetTempPath(), "ElyProxy");
		Directory.CreateDirectory(tempDir);
		_configPath = Path.Combine(tempDir, $"singbox_proxy_{Guid.NewGuid():N}.json");
		await File.WriteAllTextAsync(_configPath, configJson);
	}

	private async Task ValidateConfigurationAsync(string executablePath, string configPath)
	{
		using Process process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = executablePath,
				WorkingDirectory = (Path.GetDirectoryName(executablePath) ?? string.Empty),
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			}
		};
		process.StartInfo.ArgumentList.Add("check");
		process.StartInfo.ArgumentList.Add("-c");
		process.StartInfo.ArgumentList.Add(configPath);
		if (!process.Start())
		{
			throw new InvalidOperationException("Не удалось запустить проверку конфигурации Proxy Pro.");
		}
		using CancellationTokenSource timeout = new CancellationTokenSource(ConfigCheckTimeout);
		await process.WaitForExitAsync(timeout.Token);
		if (process.ExitCode == 0)
		{
			return;
		}
		string output = await process.StandardOutput.ReadToEndAsync();
		string error = await process.StandardError.ReadToEndAsync();
		throw new InvalidOperationException("sing-box отклонил конфигурацию Proxy Pro: " + string.Join(Environment.NewLine, new string[2] { output, error }.Where((string value) => !string.IsNullOrWhiteSpace(value))));
	}

	private void OnProcessLine(string line)
	{
		this.LogReceived?.Invoke("[sing-box] " + line);
		if (line.Contains("FATAL", StringComparison.OrdinalIgnoreCase) || line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || line.Contains("panic:", StringComparison.OrdinalIgnoreCase))
		{
			_startCompletion?.TrySetException(new InvalidOperationException(line));
		}
	}

	private void OnProcessExited(int code)
	{
		if (!_stopping)
		{
			_startCompletion?.TrySetException(new InvalidOperationException($"sing-box завершился, код: {code}"));
			CleanupConfig();
			this.StatusChanged?.Invoke(obj: false);
		}
	}

	private async Task StopProcessAsync()
	{
		_stopping = true;
		try
		{
			await _processManager.StopAsync().ConfigureAwait(false);
		}
		finally
		{
			_stopping = false;
		}
	}

	private void CleanupConfig()
	{
		if (string.IsNullOrWhiteSpace(_configPath))
		{
			return;
		}
		try
		{
			if (File.Exists(_configPath))
			{
				File.Delete(_configPath);
			}
		}
		catch
		{
		}
		_configPath = null;
	}

	public void Dispose()
	{
		StopProcessAsync().GetAwaiter().GetResult();
		CleanupConfig();
		_processManager.Dispose();
		GC.SuppressFinalize(this);
	}
}
