using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ElyProxy.Core;

public class ProcessManager : IProcessManager
{
	private Process? _process;

	private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

	public bool IsRunning
	{
		get
		{
			try
			{
				Process process = _process;
				return process != null && !process.HasExited;
			}
			catch
			{
				return false;
			}
		}
	}

	public event Action<string>? OutputReceived;

	public event Action<string>? ErrorReceived;

	public event Action<int>? ProcessExited;

	public event Action<int>? ProcessStartedForProcess;

	public event Action<int, string>? OutputReceivedForProcess;

	public event Action<int, string>? ErrorReceivedForProcess;

	public event Action<int, int>? ProcessExitedForProcess;

	public async Task StartAsync(string executablePath, string arguments, string? workingDirectory = null, bool stopExisting = true)
	{
		if (stopExisting)
		{
			await StopAsync();
		}
		ProcessStartInfo psi = new ProcessStartInfo
		{
			FileName = executablePath,
			Arguments = arguments,
			WorkingDirectory = (workingDirectory ?? Path.GetDirectoryName(executablePath) ?? ""),
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		await _lock.WaitAsync().ConfigureAwait(false);
		try
		{
			_process = new Process
			{
				StartInfo = psi,
				EnableRaisingEvents = true
			};
			_process.OutputDataReceived += OnOutput;
			_process.ErrorDataReceived += OnError;
			_process.Exited += OnExited;
			_process.Start();
			ProcessStartedForProcess?.Invoke(_process.Id);
			_process.BeginOutputReadLine();
			_process.BeginErrorReadLine();
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task StopAsync()
	{
		if (!await _lock.WaitAsync(TimeSpan.FromSeconds(2.0)).ConfigureAwait(false))
		{
			ForceKill();
			return;
		}
		try
		{
			if (_process == null)
			{
				return;
			}
			try
			{
				if (!_process.HasExited)
				{
					_process.Kill(entireProcessTree: true);
					await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5.0)).ConfigureAwait(false);
				}
			}
			catch (TimeoutException)
			{
			}
			catch
			{
			}
			finally
			{
				_process.OutputDataReceived -= OnOutput;
				_process.ErrorDataReceived -= OnError;
				_process.Exited -= OnExited;
				_process.Dispose();
				_process = null;
			}
		}
		finally
		{
			_lock.Release();
		}
	}

	private void ForceKill()
	{
		try
		{
			Process? process = _process;
			if (process != null && !process.HasExited)
			{
				process.Kill(entireProcessTree: true);
			}
		}
		catch
		{
		}
	}

	private void OnOutput(object sender, DataReceivedEventArgs e)
	{
		if (e.Data != null)
		{
			this.OutputReceived?.Invoke(e.Data);
			int processId = GetProcessId(sender);
			if (processId != 0)
			{
				OutputReceivedForProcess?.Invoke(processId, e.Data);
			}
		}
	}

	private void OnError(object sender, DataReceivedEventArgs e)
	{
		if (e.Data != null)
		{
			this.ErrorReceived?.Invoke(e.Data);
			int processId = GetProcessId(sender);
			if (processId != 0)
			{
				ErrorReceivedForProcess?.Invoke(processId, e.Data);
			}
		}
	}

	private void OnExited(object? sender, EventArgs e)
	{
		int exitCode = -1;
		int processId = GetProcessId(sender);
		try
		{
			exitCode = (sender as Process)?.ExitCode ?? (-1);
		}
		catch
		{
		}
		this.ProcessExited?.Invoke(exitCode);
		if (processId != 0)
		{
			ProcessExitedForProcess?.Invoke(processId, exitCode);
		}
	}

	private static int GetProcessId(object? sender)
	{
		try
		{
			return (sender as Process)?.Id ?? 0;
		}
		catch
		{
			return 0;
		}
	}

	public void Dispose()
	{
		StopAsync().GetAwaiter().GetResult();
		_lock.Dispose();
		GC.SuppressFinalize(this);
	}
}
