using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ElyProxy.Core;

/// <summary>
/// Keeps locally started core processes inside a Windows job object with
/// kill-on-job-close. The OS closes the job handle when ElyProxy terminates,
/// so cores die with the app even on a hard crash that never runs the normal
/// cleanup (previously they survived and kept ports like 1080 busy).
/// Cores started through the helper service are cleaned up by the service
/// itself when the client pipe session ends.
/// </summary>
public static class ProcessJob
{
    private const uint JobObjectLimitKillOnJobClose = 0x2000;
    private const int JobObjectExtendedLimitInformationClass = 9;

    private static readonly object Gate = new object();
    private static IntPtr _jobHandle;

    public static void Assign(Process process)
    {
        if (process == null)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                if (_jobHandle == IntPtr.Zero)
                {
                    _jobHandle = CreateKillOnCloseJob();
                }

                if (_jobHandle == IntPtr.Zero)
                {
                    return;
                }

                AssignProcessToJobObject(_jobHandle, process.Handle);
            }
        }
        catch
        {
        }
    }

    /// <summary>Test/diagnostic helper: whether the process belongs to our job.</summary>
    public static bool IsAssigned(Process process)
    {
        try
        {
            lock (Gate)
            {
                return _jobHandle != IntPtr.Zero
                    && IsProcessInJob(process.Handle, _jobHandle, out bool inJob)
                    && inJob;
            }
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr CreateKillOnCloseJob()
    {
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var info = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };

        int length = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        IntPtr pointer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformationClass, pointer, (uint)length))
            {
                CloseHandle(job);
                return IntPtr.Zero;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }

        return job;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
