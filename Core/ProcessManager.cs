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

	private TaskCompletionSource<bool>? _hostReady;

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
			await StopAsync().ConfigureAwait(false);
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
        var helper = ElyProxy.Services.HelperServiceInstaller.GetHelperPath();
        if (CoreHostLaunch.IsCore(executablePath) && File.Exists(helper))
            psi = CoreHostLaunch.Create(helper, executablePath, arguments, workingDirectory);
		await _lock.WaitAsync().ConfigureAwait(false);
		try
		{
            if (IsRunning) throw new InvalidOperationException("Предыдущее ядро ещё работает; повторный запуск запрещён.");
            _hostReady = psi.RedirectStandardInput ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null;
			_process = new Process
			{
				StartInfo = psi,
				EnableRaisingEvents = true
			};
			_process.OutputDataReceived += OnOutput;
			_process.ErrorDataReceived += OnError;
			_process.Exited += OnExited;
			_process.Start();
			ProcessJob.Assign(_process);
			ElyProxy.Services.LifecycleLog.Write("process", $"start {Path.GetFileName(executablePath)} host PID {_process.Id}");
            ErrorReceived?.Invoke($"[sys] Запуск ядра {Path.GetFileName(executablePath)}, PID {_process.Id}, local");
			ProcessStartedForProcess?.Invoke(_process.Id);
			_process.BeginOutputReadLine();
			_process.BeginErrorReadLine();
            if (_hostReady != null) await _hostReady.Task.WaitAsync(TimeSpan.FromSeconds(25)).ConfigureAwait(false);
		}
		finally
		{
			_lock.Release();
		}
	}

    public async Task StopAsync()
    {
        if (!await _lock.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false))
            throw new TimeoutException("Остановка ядра не выполнена: процесс занят другой операцией.");
        try
        {
            var process = _process;
            if (process == null) return;
            // Keep the handle on any failure: callers must not start a second core.
            try { await OwnedProcessStopper.StopAsync(process).ConfigureAwait(false); }
            catch
            {
                if (process.HasExited)
                {
                    process.OutputDataReceived -= OnOutput; process.ErrorDataReceived -= OnError; process.Exited -= OnExited;
                    process.Dispose(); _process = null;
                }
                throw;
            }
            process.OutputDataReceived -= OnOutput;
            process.ErrorDataReceived -= OnError;
            process.Exited -= OnExited;
            ElyProxy.Services.LifecycleLog.Write("process", $"stopped PID {process.Id}, exit {process.ExitCode}");
            process.Dispose();
            _process = null;
        }
        finally { _lock.Release(); }
    }

	private void OnOutput(object sender, DataReceivedEventArgs e)
	{
		if (e.Data != null)
		{
            if (e.Data == "[elyproxy-host-ready]") { _hostReady?.TrySetResult(true); return; }
			ElyProxy.Services.LifecycleLog.Write("core.stdout", $"pid={GetProcessId(sender)} {e.Data}");
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
			ElyProxy.Services.LifecycleLog.Write("core.stderr", $"pid={GetProcessId(sender)} {e.Data}");
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
		_hostReady?.TrySetException(new IOException("Ядро завершилось до готовности сетевой сессии."));
		int exitCode = -1;
		int processId = GetProcessId(sender);
		try
		{
			exitCode = (sender as Process)?.ExitCode ?? (-1);
		}
		catch
		{
		}
		ElyProxy.Services.LifecycleLog.Write("process.exited", $"pid={processId} code={exitCode}");
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
