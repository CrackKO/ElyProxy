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

	private readonly string _pipeName;
    private TaskCompletionSource<int>? _started;
    private TaskCompletionSource<bool>? _ready;
    private TaskCompletionSource<bool>? _stopped;
    private volatile bool _disposed;
    private bool _legacyService = true;
    private TaskCompletionSource<bool>? _hello;
    public HelperProcessManager(string pipeName = "ElyProxyHelper") { _pipeName = pipeName; }

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
			if (_activeId != null)
			{
				await StopCoreAsync().ConfigureAwait(false);
			}
			await EnsureConnectedAsync(CancellationToken.None).ConfigureAwait(false);
            try { await _hello!.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (TimeoutException) { throw new InvalidOperationException("Обновите службу-помощник: Настройки → ElyTun → Установить службу. Старая служба не подтверждает очистку сетевой сессии."); }
			string id = Guid.NewGuid().ToString("N");
			_activeId = id;
            _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
            await _started.Task.WaitAsync(TimeSpan.FromSeconds(12)).ConfigureAwait(false);
            if (!_legacyService) await _ready!.Task.WaitAsync(TimeSpan.FromSeconds(25)).ConfigureAwait(false);
            else ErrorReceived?.Invoke("[warn] Служба-помощник старой версии. Обновите её в настройках для подтверждённой очистки TUN.");
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task StopAsync()
	{
		if (!await _lock.WaitAsync(TimeSpan.FromSeconds(45)).ConfigureAwait(false))
		{
            throw new TimeoutException("Служба-помощник занята: остановка не подтверждена.");
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
        if (_activeId == null) return;
        if (_stopped?.Task.IsFaulted == true) _stopped = null;
        var stopped = _stopped ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!stopped.Task.IsCompletedSuccessfully)
        {
            if (_writer == null || _pipe is not { IsConnected: true })
                throw new IOException("Связь со службой потеряна. Завершение ядра не подтверждено.");
            var request = new HelperRequest { Operation = "stop", Id = _activeId };
            await _writer.WriteLineAsync(JsonSerializer.Serialize(request)).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            await _writer.FlushAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(40)).ConfigureAwait(false);
        _activeId = null;
        _running = false;
    }

	private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
	{
		if (_pipe is { IsConnected: true })
		{
			return;
		}
		CloseConnection();
		NamedPipeClientStream pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(3.0));
		await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
		_hello = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
                if (!ReferenceEquals(pipe, _pipe)) return;
				HandleEvent(line);
			}
		}
		catch
		{
		}
		finally
		{
            if (ReferenceEquals(pipe, _pipe))
            {
            _started?.TrySetException(new IOException("Связь со службой потеряна во время запуска."));
            // Never report stopped merely because the pipe has disconnected.
            _stopped?.TrySetException(new IOException("Связь со службой потеряна до подтверждения остановки."));
			int processId = Interlocked.Exchange(ref _activeProcessId, 0);
			if (processId != 0)
			{
				this.ProcessExited?.Invoke(-1);
				this.ProcessExitedForProcess?.Invoke(processId, -1);
			}
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
            _started?.TrySetResult(payload.ProcessId);
			break;
		case "stdout":
            if (payload.Line == "[elyproxy-host-ready]") { _ready?.TrySetResult(true); break; }
			this.OutputReceived?.Invoke(payload.Line ?? string.Empty);
			this.OutputReceivedForProcess?.Invoke(Interlocked.CompareExchange(ref _activeProcessId, 0, 0), payload.Line ?? string.Empty);
			break;
		case "stderr":
			this.ErrorReceived?.Invoke(payload.Line ?? string.Empty);
			this.ErrorReceivedForProcess?.Invoke(Interlocked.CompareExchange(ref _activeProcessId, 0, 0), payload.Line ?? string.Empty);
			break;
        case "stopped":
            _running = false;
            _stopped?.TrySetResult(true);
            break;
		case "exit":
            _ready?.TrySetException(new IOException("Ядро завершилось до готовности сетевой сессии."));
			_running = false;
            if (_legacyService) _stopped?.TrySetResult(true);
			int processId = Interlocked.Exchange(ref _activeProcessId, 0);
			this.ProcessExited?.Invoke(payload.ExitCode);
			if (processId != 0)
			{
				this.ProcessExitedForProcess?.Invoke(processId, payload.ExitCode);
			}
			break;
        case "hello":
            _legacyService = payload.Message != "stop-ack-v2";
            if (!_legacyService) _hello?.TrySetResult(true);
            break;
		case "error":
            var error = new IOException(payload.Message ?? "Ошибка службы-помощника.");
            _started?.TrySetException(error);
            _stopped?.TrySetException(error);
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
		if (_disposed) return;
        _disposed = true;
        try { StopAsync().GetAwaiter().GetResult(); }
        finally { CloseConnection(); }
		_lock.Dispose();
		GC.SuppressFinalize(this);
	}
}
