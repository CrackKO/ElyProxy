using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ElyProxy.Core;

public sealed class HelperProcessManager : IProcessManager
{
	private sealed class HelperRequest
	{
		[JsonPropertyName("op")]
		public string? Operation { get; set; }

		[JsonPropertyName("id")]
		public string? Id { get; set; }

		[JsonPropertyName("file")]
		public string? File { get; set; }

		[JsonPropertyName("args")]
		public string? Arguments { get; set; }

		[JsonPropertyName("workdir")]
		public string? WorkingDirectory { get; set; }
	}

	private sealed class HelperEvent
	{
		[JsonPropertyName("type")]
		public string? Type { get; set; }

		[JsonPropertyName("id")]
		public string? Id { get; set; }

		[JsonPropertyName("pid")]
		public int ProcessId { get; set; }

		[JsonPropertyName("code")]
		public int ExitCode { get; set; }

		[JsonPropertyName("line")]
		public string? Line { get; set; }

		[JsonPropertyName("message")]
		public string? Message { get; set; }
	}

	private const string PipeName = "ElyProxyHelper";

	private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
	private NamedPipeClientStream? _pipe;
	private StreamWriter? _writer;
	private Task? _reader;
	private string? _activeId;
	private int _activeProcessId;
	private bool _running;

	public bool IsRunning => _running;

	public event Action<string>? OutputReceived;

	public event Action<string>? ErrorReceived;

	public event Action<int>? ProcessExited;

	public event Action<int>? ProcessStartedForProcess;

	public event Action<int, string>? OutputReceivedForProcess;

	public event Action<int, string>? ErrorReceivedForProcess;

	public event Action<int, int>? ProcessExitedForProcess;

	public async Task<bool> TryConnectAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public async Task StartAsync(string executablePath, string arguments, string? workingDirectory = null, bool stopExisting = true)
	{
		if (!await _lock.WaitAsync(TimeSpan.FromSeconds(5.0)).ConfigureAwait(false))
		{
			throw new InvalidOperationException("Служба-помощник занята другим запуском ядра. Повторите через несколько секунд.");
		}
		try
		{
			if (stopExisting)
			{
				await StopCoreAsync().ConfigureAwait(false);
			}
			await EnsureConnectedAsync(CancellationToken.None).ConfigureAwait(false);
			string id = Guid.NewGuid().ToString("N");
			_activeId = id;
			_running = false;
			Interlocked.Exchange(ref _activeProcessId, 0);
			HelperRequest request = new HelperRequest
			{
				Operation = "start",
				Id = id,
				File = executablePath,
				Arguments = arguments,
				WorkingDirectory = (workingDirectory ?? Path.GetDirectoryName(executablePath) ?? string.Empty)
			};
			await _writer!.WriteLineAsync(JsonSerializer.Serialize(request)).ConfigureAwait(false);
			await _writer.FlushAsync().ConfigureAwait(false);
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
			return;
		}
		try
		{
			await StopCoreAsync().ConfigureAwait(false);
		}
		finally
		{
			_lock.Release();
		}
	}

	private async Task StopCoreAsync()
	{
		if (_writer != null && _activeId != null && _running)
		{
			try
			{
				HelperRequest request = new HelperRequest { Operation = "stop", Id = _activeId };
				await _writer.WriteLineAsync(JsonSerializer.Serialize(request)).ConfigureAwait(false);
				await _writer.FlushAsync().ConfigureAwait(false);
			}
			catch
			{
			}
		}
		_running = false;
	}

	private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
	{
		if (_pipe is { IsConnected: true })
		{
			return;
		}
		CloseConnection();
		NamedPipeClientStream pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(3.0));
		await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
		_pipe = pipe;
		_writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
		_reader = Task.Run(() => ReadLoopAsync(pipe));
	}

	private async Task ReadLoopAsync(NamedPipeClientStream pipe)
	{
		try
		{
			using StreamReader reader = new StreamReader(pipe, Encoding.UTF8);
			while (true)
			{
				string? line = await reader.ReadLineAsync().ConfigureAwait(false);
				if (line == null)
				{
					break;
				}
				HandleEvent(line);
			}
		}
		catch
		{
		}
		finally
		{
			_running = false;
			int processId = Interlocked.Exchange(ref _activeProcessId, 0);
			if (processId != 0)
			{
				this.ProcessExited?.Invoke(-1);
				this.ProcessExitedForProcess?.Invoke(processId, -1);
			}
		}
	}

	private void HandleEvent(string line)
	{
		HelperEvent? payload;
		try
		{
			payload = JsonSerializer.Deserialize<HelperEvent>(line);
		}
		catch
		{
			return;
		}
		if (payload?.Type == null || (payload.Id != null && !string.Equals(payload.Id, _activeId, StringComparison.OrdinalIgnoreCase)))
		{
			return;
		}
		switch (payload.Type)
		{
		case "started":
			_running = true;
			Interlocked.Exchange(ref _activeProcessId, payload.ProcessId);
			this.ProcessStartedForProcess?.Invoke(payload.ProcessId);
			break;
		case "stdout":
			this.OutputReceived?.Invoke(payload.Line ?? string.Empty);
			this.OutputReceivedForProcess?.Invoke(Interlocked.CompareExchange(ref _activeProcessId, 0, 0), payload.Line ?? string.Empty);
			break;
		case "stderr":
			this.ErrorReceived?.Invoke(payload.Line ?? string.Empty);
			this.ErrorReceivedForProcess?.Invoke(Interlocked.CompareExchange(ref _activeProcessId, 0, 0), payload.Line ?? string.Empty);
			break;
		case "exit":
			_running = false;
			int processId = Interlocked.Exchange(ref _activeProcessId, 0);
			this.ProcessExited?.Invoke(payload.ExitCode);
			if (processId != 0)
			{
				this.ProcessExitedForProcess?.Invoke(processId, payload.ExitCode);
			}
			break;
		case "error":
			_running = false;
			this.ErrorReceived?.Invoke(payload.Message ?? string.Empty);
			break;
		}
	}

	private void CloseConnection()
	{
		try
		{
			_writer?.Dispose();
		}
		catch
		{
		}
		try
		{
			_pipe?.Dispose();
		}
		catch
		{
		}
		_writer = null;
		_pipe = null;
	}

	public void Dispose()
	{
		StopCoreAsync().GetAwaiter().GetResult();
		CloseConnection();
		_lock.Dispose();
		GC.SuppressFinalize(this);
	}
}
