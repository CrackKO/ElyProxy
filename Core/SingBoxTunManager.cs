using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ElyProxy.Models;

namespace ElyProxy.Core;

public sealed class SingBoxTunManager : IDisposable
{
	private static readonly TimeSpan StartupProbeDelay = TimeSpan.FromSeconds(1.0);

	private static readonly TimeSpan ConfigCheckTimeout = TimeSpan.FromSeconds(8.0);

	private readonly IProcessManager _processManager;

	private readonly SingBoxConfigBuilder _configBuilder = new SingBoxConfigBuilder();

	private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);

	private readonly object _startCancellationLock = new object();

	private string? _configPath;

	private TaskCompletionSource<bool>? _startCompletion;

	private CancellationTokenSource? _startCancellation;

	private int _activeProcessId;

	public bool IsRunning => _processManager.IsRunning;

	public event Action<string>? LogReceived;

	public event Action<bool>? StatusChanged;

	public SingBoxTunManager(IProcessManager? processManager = null)
	{
		_processManager = processManager ?? new ProcessManager();
		_processManager.ProcessStartedForProcess += OnProcessStarted;
		_processManager.OutputReceivedForProcess += OnProcessLine;
		_processManager.ErrorReceivedForProcess += OnProcessLine;
		_processManager.ProcessExitedForProcess += OnProcessExited;
	}

	public string GetSingBoxPath()
	{
		string appDir = AppDomain.CurrentDomain.BaseDirectory;
		string[] candidates = new string[3]
		{
			Path.Combine(appDir, "sing-box", "sing-box.exe"),
			Path.Combine(appDir, "bin", "sing-box", "sing-box.exe"),
			Path.Combine(appDir, "sing-box.exe")
		};
		return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
	}

	public async Task StartAsync(VlessNode node, int socksPort, XrayTunOptions tunOptions)
	{
		await _lifecycleLock.WaitAsync();
		try
		{
			string singBoxPath = GetSingBoxPath();
			if (!File.Exists(singBoxPath))
			{
				throw new FileNotFoundException("sing-box не найден по пути: " + singBoxPath + "\nДобавьте sing-box.exe в папку sing-box рядом с ElyProxy.exe.", singBoxPath);
			}
			if (tunOptions.RoutingPreset == RoutingPreset.RoscomVpnDefault)
			{
				IReadOnlyList<string> missingAssets = RoscomVpnDefaultRouting.GetMissingAssetPaths();
				if (missingAssets.Count > 0)
				{
					throw new FileNotFoundException("Встроенный профиль RoscomVPN DEFAULT установлен не полностью. Переустановите сборку; отсутствует: " + string.Join(", ", missingAssets));
				}
				this.LogReceived?.Invoke("[sys] ElyTun routing: RoscomVPN DEFAULT (локальные rule-set'ы)");
			}
			await StopProcessAsync();
			CleanupConfig();
			XrayTunOptions effectiveTunOptions = await AddServerRouteExclusionsAsync(node, tunOptions);
			string configJson = _configBuilder.BuildTun(node, socksPort, effectiveTunOptions);
			await WriteConfigAsync(configJson);
			using CancellationTokenSource startCancellation = new CancellationTokenSource();
			SetStartCancellation(startCancellation);
			try
			{
				this.LogReceived?.Invoke($"[sys] ElyTun: проверка конфигурации sing-box ({ConfigCheckTimeout.TotalSeconds:0} c)");
				await ValidateConfigurationAsync(singBoxPath, _configPath, startCancellation.Token);
				PrepareStartWait();
				this.LogReceived?.Invoke("[sys] ElyTun core: sing-box (" + singBoxPath + ")");
				this.LogReceived?.Invoke($"[sys] ElyTun: auto route, {effectiveTunOptions.InterfaceName}, MTU {effectiveTunOptions.Mtu}");
				this.LogReceived?.Invoke((effectiveTunOptions.RoutingPreset == RoutingPreset.RoscomVpnDefault) ? "[sys] ElyTun DNS: RoscomVPN DEFAULT (Yandex direct, Google DoH через прокси)" : ("[sys] ElyTun DNS: bypass → " + (effectiveTunOptions.DnsServers.FirstOrDefault() ?? "авто") + ", остальное → DoH через прокси"));
				string workingDirectory = Path.GetDirectoryName(singBoxPath);
				await _processManager.StartAsync(singBoxPath, "run -c \"" + _configPath + "\"", workingDirectory, stopExisting: false);
				await EnsureStartedAsync(startCancellation.Token);
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
				ClearStartCancellation(startCancellation);
			}
		}
		finally
		{
			_lifecycleLock.Release();
		}
	}

	public async Task StopAsync()
	{
		CancelPendingStart();
		await _lifecycleLock.WaitAsync();
		try
		{
			await StopProcessAsync();
			CleanupConfig();
			this.StatusChanged?.Invoke(obj: false);
			this.LogReceived?.Invoke("[sys] sing-box ElyTun остановлен");
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
		_configPath = Path.Combine(tempDir, $"singbox_{Guid.NewGuid():N}.json");
		await File.WriteAllTextAsync(_configPath, configJson);
	}

	private async Task<XrayTunOptions> AddServerRouteExclusionsAsync(VlessNode node, XrayTunOptions options)
	{
		List<IPAddress> addresses = new List<IPAddress>();
		if (IPAddress.TryParse(node.Address, out IPAddress literal))
		{
			addresses.Add(literal);
		}
		else if (!string.IsNullOrWhiteSpace(node.Address))
		{
			try
			{
				using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3.0));
				addresses.AddRange(await Dns.GetHostAddressesAsync(node.Address, cancellation.Token));
			}
			catch (OperationCanceledException)
			{
				this.LogReceived?.Invoke("[warn] Не удалось вовремя определить IP VLESS-сервера: " + node.Address + ". Запуск продолжается.");
			}
			catch (Exception ex2)
			{
				this.LogReceived?.Invoke($"[warn] Не удалось определить IP VLESS-сервера: {node.Address} ({ex2.GetType().Name}). Запуск продолжается.");
			}
		}
		string[] endpointCidrs = (from address in addresses
			where !IPAddress.IsLoopback(address)
			select $"{address}/{((address.AddressFamily == AddressFamily.InterNetwork) ? 32 : 128)}").Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		if (endpointCidrs.Length == 0)
		{
			return options;
		}
		string[] exclusions = options.DirectIpCidrs.Concat(endpointCidrs).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		this.LogReceived?.Invoke("[sys] ElyTun: маршрут VLESS-сервера оставлен вне TUN: " + string.Join(", ", endpointCidrs));
		return options with
		{
			DirectIpCidrs = exclusions
		};
	}

	private void PrepareStartWait()
	{
		_startCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	private async Task EnsureStartedAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource<bool> startCompletion = _startCompletion ?? throw new InvalidOperationException("Ожидание запуска sing-box не было подготовлено.");
		if (await Task.WhenAny(startCompletion.Task, Task.Delay(StartupProbeDelay, cancellationToken)) == startCompletion.Task)
		{
			await startCompletion.Task;
			return;
		}
		cancellationToken.ThrowIfCancellationRequested();
		if (!_processManager.IsRunning)
		{
			throw new InvalidOperationException("sing-box завершился сразу после запуска. Проверьте лог выше.");
		}
		_startCompletion = null;
	}

	private void OnProcessStarted(int processId)
	{
		Interlocked.Exchange(ref _activeProcessId, processId);
	}

	private void OnProcessLine(int processId, string line)
	{
		if (Volatile.Read(ref _activeProcessId) != processId)
		{
			return;
		}

		this.LogReceived?.Invoke("[sing-box] " + line);
		if (line.Contains("sing-box started", StringComparison.OrdinalIgnoreCase))
		{
			_startCompletion?.TrySetResult(result: true);
		}
		else if (line.Contains("FATAL", StringComparison.OrdinalIgnoreCase) || line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || line.Contains("panic:", StringComparison.OrdinalIgnoreCase))
		{
			_startCompletion?.TrySetException(new InvalidOperationException(line));
		}
	}

	private void OnProcessExited(int processId, int code)
	{
		if (processId != 0 && Interlocked.CompareExchange(ref _activeProcessId, 0, processId) == processId)
		{
			_startCompletion?.TrySetException(new InvalidOperationException($"sing-box завершился при запуске, код: {code}"));
			this.LogReceived?.Invoke($"[sys] sing-box завершён, код: {code}");
			CleanupConfig();
			this.StatusChanged?.Invoke(obj: false);
		}
	}

	private async Task StopProcessAsync()
	{
		Interlocked.Exchange(ref _activeProcessId, 0);
		await _processManager.StopAsync();
	}

	private async Task ValidateConfigurationAsync(string executablePath, string configPath, CancellationToken cancellationToken)
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
				RedirectStandardError = true,
				StandardOutputEncoding = Encoding.UTF8,
				StandardErrorEncoding = Encoding.UTF8
			}
		};
		process.StartInfo.ArgumentList.Add("check");
		process.StartInfo.ArgumentList.Add("-c");
		process.StartInfo.ArgumentList.Add(configPath);
		if (!process.Start())
		{
			throw new InvalidOperationException("Не удалось запустить проверку конфигурации sing-box.");
		}
		Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
		Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(ConfigCheckTimeout);
		try
		{
			await process.WaitForExitAsync(timeout.Token);
			string output = await outputTask;
			string error = await errorTask;
			if (process.ExitCode == 0)
			{
				return;
			}
			string details = string.Join(Environment.NewLine, new string[2] { output, error }.Where((string value) => !string.IsNullOrWhiteSpace(value))).Trim();
			throw new InvalidOperationException($"sing-box отклонил конфигурацию TUN (код {process.ExitCode})" + (string.IsNullOrWhiteSpace(details) ? "." : (": " + details)));
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new TimeoutException($"Проверка конфигурации sing-box не завершилась за {ConfigCheckTimeout.TotalSeconds:0} секунд.");
		}
		finally
		{
			try
			{
				if (!process.HasExited)
				{
					process.Kill(entireProcessTree: true);
				}
			}
			catch
			{
			}
		}
	}

	private void SetStartCancellation(CancellationTokenSource cancellation)
	{
		lock (_startCancellationLock)
		{
			_startCancellation = cancellation;
		}
	}

	private void ClearStartCancellation(CancellationTokenSource cancellation)
	{
		lock (_startCancellationLock)
		{
			if (_startCancellation == cancellation)
			{
				_startCancellation = null;
			}
		}
	}

	private void CancelPendingStart()
	{
		lock (_startCancellationLock)
		{
			_startCancellation?.Cancel();
		}
	}

	private void CleanupConfig()
	{
		if (_configPath == null)
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
		CancelPendingStart();
		StopProcessAsync().GetAwaiter().GetResult();
		CleanupConfig();
		_processManager.Dispose();
		GC.SuppressFinalize(this);
	}
}
