using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElyProxy.Models;

namespace ElyProxy.Core;

public sealed class HysteriaManager : IDisposable
{
	private static readonly TimeSpan StartupProbeDelay = TimeSpan.FromSeconds(4.0);

	private readonly IProcessManager _processManager;

	private readonly HysteriaConfigBuilder _configBuilder = new HysteriaConfigBuilder();

	private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);

	private string? _configPath;

	private TaskCompletionSource<bool>? _startCompletion;

	private bool _stopping;

	public bool IsRunning => _processManager.IsRunning;

	public string? LastConfigHash { get; private set; }

	public event Action<string>? LogReceived;

	public event Action<bool>? StatusChanged;

	public HysteriaManager(IProcessManager? processManager = null)
	{
		_processManager = processManager ?? new ProcessManagerRouter();
		_processManager.OutputReceived += OnProcessLine;
		_processManager.ErrorReceived += OnProcessLine;
		_processManager.ProcessExited += OnProcessExited;
	}

	public string GetHysteriaPath()
	{
		return CoreLocator.Resolve("hysteria", "hysteria.exe", "hysteria", Path.Combine("bin", "hysteria"), string.Empty);
	}

	public Task StartAsync(VlessNode node, int socksPort, int httpPort = 0)
	{
		return StartCoreAsync(
			() => _configBuilder.Build(node, socksPort, httpPort),
			$"[sys] Hysteria 2: SOCKS5 127.0.0.1:{socksPort}" + (httpPort > 0 ? $", HTTP 127.0.0.1:{httpPort}" : string.Empty));
	}

	public Task StartTunAsync(VlessNode node, XrayTunOptions options)
	{
		return StartCoreAsync(
			() => _configBuilder.BuildTun(node, options),
			$"[sys] Hysteria 2 TUN: {options.InterfaceName}, MTU {options.Mtu}");
	}

	private async Task StartCoreAsync(Func<string> buildConfig, string description)
	{
		await _lifecycleLock.WaitAsync();
		try
		{
			string hysteriaPath = GetHysteriaPath();
			if (!File.Exists(hysteriaPath))
			{
				throw new FileNotFoundException("Ядро Hysteria 2 не найдено по пути: " + hysteriaPath + "\nОбновите ядра в настройках или добавьте hysteria.exe в папку hysteria рядом с ElyProxy.exe.", hysteriaPath);
			}
			await StopProcessAsync();
			CleanupConfig();
			string config = buildConfig();
			LastConfigHash = ConfigHash.Compute(config);
			this.LogReceived?.Invoke("[sys] Хеш конфига: " + LastConfigHash);
			await WriteConfigAsync(config);
			_startCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			this.LogReceived?.Invoke("[sys] TUN core: Hysteria 2 (" + hysteriaPath + ")");
			this.LogReceived?.Invoke(description);
			string workingDirectory = Path.GetDirectoryName(hysteriaPath);
			await _processManager.StartAsync(hysteriaPath, "client -c \"" + _configPath + "\"", workingDirectory, stopExisting: false);
			if (await Task.WhenAny(_startCompletion.Task, Task.Delay(StartupProbeDelay)) == _startCompletion.Task)
			{
				await _startCompletion.Task;
			}
			else if (!_processManager.IsRunning)
			{
				throw new InvalidOperationException("Hysteria 2 завершился сразу после запуска. Проверьте лог выше.");
			}
			_startCompletion = null;
			this.StatusChanged?.Invoke(obj: true);
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
			this.LogReceived?.Invoke("[sys] Hysteria 2 остановлен");
		}
		finally
		{
			_lifecycleLock.Release();
		}
	}

	private async Task WriteConfigAsync(string yaml)
	{
		string tempDir = Path.Combine(Path.GetTempPath(), "ElyProxy");
		Directory.CreateDirectory(tempDir);
		_configPath = Path.Combine(tempDir, $"hysteria_{Guid.NewGuid():N}.yaml");
		await File.WriteAllTextAsync(_configPath, yaml);
	}

	private void OnProcessLine(string line)
	{
		this.LogReceived?.Invoke("[hysteria] " + line);
		if (line.Contains("connected to server", StringComparison.OrdinalIgnoreCase) || line.Contains("client connected", StringComparison.OrdinalIgnoreCase))
		{
			_startCompletion?.TrySetResult(result: true);
		}
		else if (line.Contains("fatal", StringComparison.OrdinalIgnoreCase) || line.Contains("panic:", StringComparison.OrdinalIgnoreCase))
		{
			_startCompletion?.TrySetException(new InvalidOperationException(line));
		}
	}

	private void OnProcessExited(int code)
	{
		if (!_stopping)
		{
			_startCompletion?.TrySetException(new InvalidOperationException($"Hysteria 2 завершился, код: {code}"));
			this.LogReceived?.Invoke($"[sys] Hysteria 2 завершён, код: {code}");
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
