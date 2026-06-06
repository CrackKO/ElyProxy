using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ElyProxy.Services;

public static class PortOwnerService
{
    private const int AfInet = 2;
    private const int TcpTableOwnerPidAll = 5;
    private const uint MibTcpStateListen = 2;

    public static bool TryStopStaleXrayOnPort(int port, string expectedXrayPath, out string message)
    {
        message = string.Empty;
        var pid = GetTcpListenOwnerPid(port);
        if (!pid.HasValue)
            return false;

        Process? process = null;
        try
        {
            process = Process.GetProcessById(pid.Value);
            var processPath = GetProcessPath(process);

            if (!IsExpectedXray(process, processPath, expectedXrayPath))
            {
                message = FormatPortOwner(port, process, processPath);
                return false;
            }

            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(3000))
            {
                message = $"SOCKS5 порт 127.0.0.1:{port} занят зависшим Xray, но его не удалось остановить.";
                return false;
            }

            message = $"[sys] Остановлен зависший Xray на 127.0.0.1:{port} (PID {pid.Value})";
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
        var pid = GetTcpListenOwnerPid(port);
        if (!pid.HasValue)
            return "процесс не определён";

        Process? process = null;
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
        var bufferSize = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AfInet, TcpTableOwnerPidAll, 0);

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            var result = GetExtendedTcpTable(buffer, ref bufferSize, true, AfInet, TcpTableOwnerPidAll, 0);
            if (result != 0)
                return null;

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPtr = IntPtr.Add(buffer, sizeof(int));
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();

            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
                if (row.State == MibTcpStateListen && row.LocalPort == port)
                    return row.OwningPid;

                rowPtr = IntPtr.Add(rowPtr, rowSize);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return null;
    }

    private static bool IsExpectedXray(Process process, string? processPath, string expectedXrayPath)
    {
        if (!string.Equals(process.ProcessName, "xray", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(processPath))
            return false;

        return string.Equals(
            NormalizePath(processPath),
            NormalizePath(expectedXrayPath),
            StringComparison.OrdinalIgnoreCase);
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
        var name = string.IsNullOrWhiteSpace(process.ProcessName) ? "process" : process.ProcessName;
        var pathPart = string.IsNullOrWhiteSpace(path) ? string.Empty : $" ({path})";
        return $"SOCKS5 порт 127.0.0.1:{port} занят: {name}, PID {process.Id}{pathPart}";
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int tcpTableLength,
        bool sort,
        int ipVersion,
        int tableClass,
        uint reserved);

    [StructLayout(LayoutKind.Sequential)]
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

        public int LocalPort => BitConverter.ToUInt16([_localPort2, _localPort1], 0);
    }
}
