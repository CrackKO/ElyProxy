using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElyProxy.Helper;

internal static class HelperProtocol
{
	public const string PipeName = "ElyProxyHelper";

	public static readonly string[] AllowedExecutables = new string[4]
	{
		"xray.exe",
		"sing-box.exe",
		"tun2proxy-bin.exe",
		"hysteria.exe"
	};

	public static bool IsAllowedExecutable(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return false;
		}
		string fileName = Path.GetFileName(path);
		foreach (string allowed in AllowedExecutables)
		{
			if (string.Equals(fileName, allowed, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}
}

internal sealed class HelperRequest
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

internal sealed class HelperEvent
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

	public static HelperEvent Started(string id, int pid) => new HelperEvent { Type = "started", Id = id, ProcessId = pid };

	public static HelperEvent Output(string id, string line) => new HelperEvent { Type = "stdout", Id = id, Line = line };

	public static HelperEvent Error(string id, string line) => new HelperEvent { Type = "stderr", Id = id, Line = line };

	public static HelperEvent Exited(string id, int code) => new HelperEvent { Type = "exit", Id = id, ExitCode = code };

	public static HelperEvent Failure(string id, string message) => new HelperEvent { Type = "error", Id = id, Message = message };
}

internal sealed class HelperServer
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
	private Task? _acceptLoop;

	public void Start()
	{
		_acceptLoop = Task.Run(() => AcceptLoopAsync(_cancellation.Token));
	}

	public void Stop()
	{
		try
		{
			_cancellation.Cancel();
		}
		catch
		{
		}
	}

	private async Task AcceptLoopAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			NamedPipeServerStream? pipe = null;
			try
			{
				pipe = CreatePipe();
				await pipe.WaitForConnectionAsync(cancellationToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch
			{
				pipe?.Dispose();
				await Task.Delay(500, CancellationToken.None);
				continue;
			}
			NamedPipeServerStream connected = pipe;
			_ = Task.Run(() => SessionLoopAsync(connected), CancellationToken.None);
		}
	}

	private static NamedPipeServerStream CreatePipe()
	{
		PipeSecurity security = new PipeSecurity();
		SecurityIdentifier system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
		SecurityIdentifier administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
		SecurityIdentifier authenticatedUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
		security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));
		security.AddAccessRule(new PipeAccessRule(administrators, PipeAccessRights.FullControl, AccessControlType.Allow));
		security.AddAccessRule(new PipeAccessRule(authenticatedUsers, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
		return NamedPipeServerStreamAcl.Create(
			HelperProtocol.PipeName,
			PipeDirection.InOut,
			NamedPipeServerStream.MaxAllowedServerInstances,
			PipeTransmissionMode.Byte,
			PipeOptions.Asynchronous,
			0,
			0,
			security);
	}

	private static async Task SessionLoopAsync(NamedPipeServerStream pipe)
	{
		Dictionary<string, Process> processes = new Dictionary<string, Process>(StringComparer.OrdinalIgnoreCase);
		SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);
		try
		{
			using StreamReader reader = new StreamReader(pipe);
			using StreamWriter writer = new StreamWriter(pipe) { AutoFlush = true };
			while (true)
			{
				string? line = await reader.ReadLineAsync();
				if (line == null)
				{
					break;
				}
				HelperRequest? request = null;
				try
				{
					request = JsonSerializer.Deserialize<HelperRequest>(line);
				}
				catch
				{
				}
				if (request?.Operation == null)
				{
					continue;
				}
				switch (request.Operation)
				{
				case "start":
					await HandleStartAsync(request, processes, writer, writeLock);
					break;
				case "stop":
					HandleStop(request.Id, processes);
					break;
				case "stopall":
					HandleStopAll(processes);
					break;
				}
			}
		}
		catch
		{
		}
		finally
		{
			HandleStopAll(processes);
			try
			{
				pipe.Dispose();
			}
			catch
			{
			}
			writeLock.Dispose();
		}
	}

	private static async Task HandleStartAsync(
		HelperRequest request,
		Dictionary<string, Process> processes,
		StreamWriter writer,
		SemaphoreSlim writeLock)
	{
		string id = (request.Id ?? Guid.NewGuid().ToString("N"));
		if (!HelperProtocol.IsAllowedExecutable(request.File ?? string.Empty))
		{
			await WriteEventAsync(writer, writeLock, HelperEvent.Failure(id, "Недопустимый исполняемый файл для службы-помощника."));
			return;
		}
		try
		{
			HandleStop(id, processes);
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = request.File,
				Arguments = (request.Arguments ?? string.Empty),
				WorkingDirectory = (string.IsNullOrWhiteSpace(request.WorkingDirectory) ? Path.GetDirectoryName(request.File) ?? string.Empty : request.WorkingDirectory),
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				StandardOutputEncoding = System.Text.Encoding.UTF8,
				StandardErrorEncoding = System.Text.Encoding.UTF8
			};
			Process process = new Process
			{
				StartInfo = startInfo,
				EnableRaisingEvents = true
			};
			process.OutputDataReceived += (_, args) =>
			{
				if (args.Data != null)
				{
					_ = WriteEventAsync(writer, writeLock, HelperEvent.Output(id, args.Data));
				}
			};
			process.ErrorDataReceived += (_, args) =>
			{
				if (args.Data != null)
				{
					_ = WriteEventAsync(writer, writeLock, HelperEvent.Error(id, args.Data));
				}
			};
			process.Exited += (_, _) =>
			{
				int code = -1;
				try
				{
					code = process.ExitCode;
				}
				catch
				{
				}
				_ = WriteEventAsync(writer, writeLock, HelperEvent.Exited(id, code));
			};
			if (!process.Start())
			{
				await WriteEventAsync(writer, writeLock, HelperEvent.Failure(id, "Не удалось запустить процесс."));
				return;
			}
			processes[id] = process;
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			await WriteEventAsync(writer, writeLock, HelperEvent.Started(id, process.Id));
		}
		catch (Exception ex)
		{
			await WriteEventAsync(writer, writeLock, HelperEvent.Failure(id, ex.Message));
		}
	}

	private static void HandleStop(string? id, Dictionary<string, Process> processes)
	{
		if (string.IsNullOrWhiteSpace(id) || !processes.TryGetValue(id, out Process? process))
		{
			return;
		}
		processes.Remove(id);
		KillProcess(process);
	}

	private static void HandleStopAll(Dictionary<string, Process> processes)
	{
		foreach (Process process in processes.Values)
		{
			KillProcess(process);
		}
		processes.Clear();
	}

	private static void KillProcess(Process process)
	{
		try
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
				process.WaitForExit(5000);
			}
		}
		catch
		{
		}
		finally
		{
			try
			{
				process.Dispose();
			}
			catch
			{
			}
		}
	}

	private static async Task WriteEventAsync(StreamWriter writer, SemaphoreSlim writeLock, HelperEvent payload)
	{
		string json = JsonSerializer.Serialize(payload, JsonOptions);
		await writeLock.WaitAsync();
		try
		{
			await writer.WriteLineAsync(json);
		}
		catch
		{
		}
		finally
		{
			writeLock.Release();
		}
	}
}
