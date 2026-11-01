using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ElyProxy.Services;

public static class PortOwnerService
{
	private readonly struct MibTcpRowOwnerPid
	{
		public readonly uint State;

		public readonly uint LocalAddress;

		private readonly byte _localPort1;

		private readonly byte _localPort2;

		private readonly byte _localPort3;

		private readonly byte _localPort4;

		public readonly uint RemoteAddress;

		private readonly byte _remotePort1;

		private readonly byte _remotePort2;

		private readonly byte _remotePort3;

		private readonly byte _remotePort4;

		public readonly int OwningPid;

		public int LocalPort => BitConverter.ToUInt16(new byte[2] { _localPort2, _localPort1 }, 0);
	}

	private const int AfInet = 2;

	private const int TcpTableOwnerPidAll = 5;

	private const uint MibTcpStateListen = 2u;

	public static bool TryStopStaleXrayOnPort(int port, string expectedXrayPath, out string message)
	{
		return TryStopStaleCoreOnPort(port, expectedXrayPath, out message);
	}

	public static bool TryStopStaleCoreOnPort(int port, string expectedCorePath, out string message)
	{
		message = string.Empty;
		int? pid = GetTcpListenOwnerPid(port);
		if (!pid.HasValue)
		{
			return false;
		}
		Process process = null;
		try
		{
			process = Process.GetProcessById(pid.Value);
			string processPath = GetProcessPath(process);
			if (!IsExpectedCore(process, processPath, expectedCorePath))
			{
				message = FormatPortOwner(port, process, processPath);
				return false;
			}
			process.Kill(entireProcessTree: true);
			if (!process.WaitForExit(3000))
			{
				message = $"SOCKS5 порт 127.0.0.1:{port} занят зависшим proxy-core, но его не удалось остановить.";
				return false;
			}
			message = $"[sys] Остановлен зависший proxy-core на 127.0.0.1:{port} (PID {pid.Value})";
			return true;
		}
		catch (Exception ex)
		{
			message = $"SOCKS5 порт 127.0.0.1:{port} занят, не удалось проверить процесс: {ex.Message}";
			return false;
		}
		finally
		{
			process?.Dispose();
		}
	}

	public static string DescribeTcpPortOwner(int port)
	{
		int? pid = GetTcpListenOwnerPid(port);
		if (!pid.HasValue)
		{
			return "процесс не определён";
		}
		Process process = null;
		try
		{
			process = Process.GetProcessById(pid.Value);
			return FormatPortOwner(port, process, GetProcessPath(process));
		}
		catch
		{
			return $"PID {pid.Value}";
		}
		finally
		{
			process?.Dispose();
		}
	}

	private static int? GetTcpListenOwnerPid(int port)
	{
		int bufferSize = 0;
		GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, sort: true, 2, 5, 0u);
		nint buffer = Marshal.AllocHGlobal(bufferSize);
		try
		{
			if (GetExtendedTcpTable(buffer, ref bufferSize, sort: true, 2, 5, 0u) != 0)
			{
				return null;
			}
			int rowCount = Marshal.ReadInt32(buffer);
			nint rowPtr = IntPtr.Add(buffer, 4);
			int rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
			for (int i = 0; i < rowCount; i++)
			{
				MibTcpRowOwnerPid row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
				if (row.State == 2 && row.LocalPort == port)
				{
					return row.OwningPid;
				}
				rowPtr = IntPtr.Add(rowPtr, rowSize);
			}
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}
		return null;
	}

	private static bool IsExpectedCore(Process process, string? processPath, string expectedCorePath)
	{
		string expectedProcessName = Path.GetFileNameWithoutExtension(expectedCorePath);
		if (!string.Equals(process.ProcessName, expectedProcessName, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(processPath))
		{
			return false;
		}
		return string.Equals(NormalizePath(processPath), NormalizePath(expectedCorePath), StringComparison.OrdinalIgnoreCase);
	}

	private static string? GetProcessPath(Process process)
	{
		try
		{
			return process.MainModule?.FileName;
		}
		catch
		{
			return null;
		}
	}

	private static string FormatPortOwner(int port, Process process, string? path)
	{
		string name = (string.IsNullOrWhiteSpace(process.ProcessName) ? "process" : process.ProcessName);
		string pathPart = (string.IsNullOrWhiteSpace(path) ? string.Empty : (" (" + path + ")"));
		return $"SOCKS5 порт 127.0.0.1:{port} занят: {name}, PID {process.Id}{pathPart}";
	}

	private static string NormalizePath(string path)
	{
		return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
	}

	[DllImport("iphlpapi.dll", SetLastError = true)]
	private static extern uint GetExtendedTcpTable(nint tcpTable, ref int tcpTableLength, bool sort, int ipVersion, int tableClass, uint reserved);
}
