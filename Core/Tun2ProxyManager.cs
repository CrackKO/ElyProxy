using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ElyProxy.Models;

namespace ElyProxy.Core;

public sealed class Tun2ProxyManager : IDisposable
{
	private static readonly TimeSpan StartupProbeDelay = TimeSpan.FromSeconds(3.0);

	private readonly IProcessManager _processManager;

	private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);

	private TaskCompletionSource<bool>? _startCompletion;

	private bool _stopping;

	public bool IsRunning => _processManager.IsRunning;

	public event Action<string>? LogReceived;

	public event Action<bool>? StatusChanged;

	public Tun2ProxyManager(IProcessManager? processManager = null)
	{
		_processManager = processManager ?? new ProcessManagerRouter();
		_processManager.OutputReceived += OnProcessLine;
		_processManager.ErrorReceived += OnProcessLine;
		_processManager.ProcessExited += OnProcessExited;
	}

	public string GetTun2ProxyPath()
	{
		return CoreLocator.Resolve("tun2proxy", "tun2proxy-bin.exe", "tun2proxy", Path.Combine("bin", "tun2proxy"), string.Empty);
	}

	public async Task StartAsync(int socksPort, XrayTunOptions options, IReadOnlyList<string> extraBypassCidrs)
	{
		await _lifecycleLock.WaitAsync();
		try
		{
			string tun2ProxyPath = GetTun2ProxyPath();
			if (!File.Exists(tun2ProxyPath))
			{
				throw new FileNotFoundException("tun2proxy не найден по пути: " + tun2ProxyPath + "\nДобавьте tun2proxy-bin.exe в папку tun2proxy рядом с ElyProxy.exe.", tun2ProxyPath);
			}
			await StopProcessAsync();
			string arguments = BuildArguments(socksPort, options, extraBypassCidrs);
			_startCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			this.LogReceived?.Invoke("[sys] TUN core: tun2proxy (" + tun2ProxyPath + ")");
			this.LogReceived?.Invoke("[sys] tun2proxy: " + arguments);
			await _processManager.StartAsync(tun2ProxyPath, arguments, ResolveWorkingDirectory(tun2ProxyPath), stopExisting: false);
			if (await Task.WhenAny(_startCompletion.Task, Task.Delay(StartupProbeDelay)) == _startCompletion.Task)
			{
				await _startCompletion.Task;
			}
			else if (!_processManager.IsRunning)
			{
				throw new InvalidOperationException("tun2proxy завершился сразу после запуска. Проверьте лог выше.");
			}
			_startCompletion = null;
			this.StatusChanged?.Invoke(obj: true);
		}
		catch
		{
			await StopProcessAsync();
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
			this.StatusChanged?.Invoke(obj: false);
			this.LogReceived?.Invoke("[sys] tun2proxy остановлен");
		}
		finally
		{
			_lifecycleLock.Release();
		}
	}

	private static string BuildArguments(int socksPort, XrayTunOptions options, IReadOnlyList<string> extraBypassCidrs)
	{
		StringBuilder builder = new StringBuilder();
		builder.Append("--proxy socks5://127.0.0.1:").Append(socksPort);
		builder.Append(" --setup --dns virtual --exit-on-fatal-error");
		string interfaceName = (string.IsNullOrWhiteSpace(options.InterfaceName) ? "ElyTun" : options.InterfaceName);
		builder.Append(" --tun \"").Append(interfaceName).Append('"');
		if (options.IncludeIpv6)
		{
			builder.Append(" --ipv6-enabled");
		}
		foreach (string cidr in extraBypassCidrs.Concat(options.DirectIpCidrs).Where(IsIpOrCidr).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			builder.Append(" --bypass ").Append(cidr);
		}
		return builder.ToString();
	}

	private static string ResolveWorkingDirectory(string executablePath)
	{
		string? directory = Path.GetDirectoryName(executablePath);
		if (directory != null && File.Exists(Path.Combine(directory, "wintun.dll")))
		{
			return directory;
		}
		string xrayDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "xray");
		if (File.Exists(Path.Combine(xrayDirectory, "wintun.dll")))
		{
			return xrayDirectory;
		}
		return directory ?? AppDomain.CurrentDomain.BaseDirectory;
	}

	private void OnProcessLine(string line)
	{
		this.LogReceived?.Invoke("[tun2proxy] " + line);
	}

	private void OnProcessExited(int code)
	{
		if (!_stopping)
		{
			_startCompletion?.TrySetException(new InvalidOperationException($"tun2proxy завершился, код: {code}"));
			this.LogReceived?.Invoke($"[sys] tun2proxy завершён, код: {code}");
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

	private static bool IsIpOrCidr(string value)
	{
		int slash = value.LastIndexOf('/');
		if (!IPAddress.TryParse((slash >= 0) ? value.Substring(0, slash) : value, out IPAddress address))
		{
			return false;
		}
		if (slash < 0)
		{
			return true;
		}
		int num = slash + 1;
		return int.TryParse(value.Substring(num, value.Length - num), out int prefix) && prefix >= 0 && prefix <= address.GetAddressBytes().Length * 8;
	}

	public void Dispose()
	{
		StopProcessAsync().GetAwaiter().GetResult();
		_processManager.Dispose();
		GC.SuppressFinalize(this);
	}
}
