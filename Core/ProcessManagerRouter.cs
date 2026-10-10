using System;
using System.Threading.Tasks;

namespace ElyProxy.Core;

public sealed class ProcessManagerRouter : IProcessManager
{
	private readonly ProcessManager _local = new ProcessManager();
	private readonly HelperProcessManager _helper = new HelperProcessManager();
	private IProcessManager _active;

	public ProcessManagerRouter()
	{
		_active = _local;
		_local.OutputReceived += message => this.OutputReceived?.Invoke(message);
		_local.ErrorReceived += message => this.ErrorReceived?.Invoke(message);
		_local.ProcessExited += code => this.ProcessExited?.Invoke(code);
		_local.ProcessStartedForProcess += processId => this.ProcessStartedForProcess?.Invoke(processId);
		_local.OutputReceivedForProcess += (processId, message) => this.OutputReceivedForProcess?.Invoke(processId, message);
		_local.ErrorReceivedForProcess += (processId, message) => this.ErrorReceivedForProcess?.Invoke(processId, message);
		_local.ProcessExitedForProcess += (processId, code) => this.ProcessExitedForProcess?.Invoke(processId, code);
		_helper.OutputReceived += message => this.OutputReceived?.Invoke(message);
		_helper.ErrorReceived += message => this.ErrorReceived?.Invoke(message);
		_helper.ProcessExited += code => this.ProcessExited?.Invoke(code);
		_helper.ProcessStartedForProcess += processId => this.ProcessStartedForProcess?.Invoke(processId);
		_helper.OutputReceivedForProcess += (processId, message) => this.OutputReceivedForProcess?.Invoke(processId, message);
		_helper.ErrorReceivedForProcess += (processId, message) => this.ErrorReceivedForProcess?.Invoke(processId, message);
		_helper.ProcessExitedForProcess += (processId, code) => this.ProcessExitedForProcess?.Invoke(processId, code);
	}

	public bool IsRunning => _active.IsRunning;

	public event Action<string>? OutputReceived;

	public event Action<string>? ErrorReceived;

	public event Action<int>? ProcessExited;

	public event Action<int>? ProcessStartedForProcess;

	public event Action<int, string>? OutputReceivedForProcess;

	public event Action<int, string>? ErrorReceivedForProcess;

	public event Action<int, int>? ProcessExitedForProcess;

	public async Task StartAsync(string executablePath, string arguments, string? workingDirectory = null, bool stopExisting = true)
	{
		IProcessManager target = _local;
		if (HelperRuntime.UseHelperService)
		{
			if (await _helper.TryConnectAsync().ConfigureAwait(false))
			{
				target = _helper;
			}
			else
			{
				HelperRuntime.LastFallbackReason = "Служба-помощник ElyProxy недоступна; TUN запущен напрямую (может потребоваться UAC).";
			}
		}
		_active = target;
		await target.StartAsync(executablePath, arguments, workingDirectory, stopExisting).ConfigureAwait(false);
	}

	public async Task StopAsync()
	{
		await _active.StopAsync().ConfigureAwait(false);
	}

	public void Dispose()
	{
		_local.Dispose();
		_helper.Dispose();
		GC.SuppressFinalize(this);
	}
}
