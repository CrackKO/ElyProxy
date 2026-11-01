using System.Runtime.InteropServices;

namespace ElyProxy.Services;

public static class ProbeExecutionPolicy
{
    // Memory is a capacity budget for independent core processes, not an allocation target.
    public static int Resolve(int requested, int processors, ulong availableBytes)
    {
        const ulong mib = 1024 * 1024;
        int memorySlots = availableBytes <= 1024 * mib ? 1 : (int)Math.Min(16UL, (availableBytes - 1024 * mib) / (384 * mib));
        return Math.Max(1, Math.Min(Math.Clamp(requested, 1, 16), Math.Min(Math.Max(1, processors * 2), memorySlots)));
    }
    public static int Current(int requested)
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        ulong available = GlobalMemoryStatusEx(ref status) ? status.AvailablePhysical : 2UL * 1024 * 1024 * 1024;
        return Resolve(requested, Environment.ProcessorCount, available);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, ExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
