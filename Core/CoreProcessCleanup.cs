using System;
using System.Diagnostics;
using System.IO;

namespace ElyProxy.Core;

/// <summary>
/// Kills leftover core processes from a previous crashed run. They keep
/// ports like 1080 busy and block new connections until removed manually.
/// Only processes whose executable lives inside the ElyProxy app folder or
/// the local cores folder are touched; helper-owned SYSTEM processes are
/// skipped (the helper service cleans those up when its session ends).
/// </summary>
public static class CoreProcessCleanup
{
    private static readonly string[] CoreProcessNames = { "xray", "sing-box", "tun2proxy-bin", "hysteria" };

    public static int KillStaleCores()
    {
        int killed = 0;
        foreach (string name in CoreProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (Process process in processes)
            {
                try
                {
                    string? path = TryGetPath(process);
                    if (path != null && IsOurCorePath(path, AppDomain.CurrentDomain.BaseDirectory, CoreLocator.CoresRoot))
                    {
                        process.Kill(entireProcessTree: true);
                        killed++;
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return killed;
    }

    public static bool IsOurCorePath(string path, string appDirectory, string coresRoot)
    {
        return StartsWithDirectory(path, appDirectory) || StartsWithDirectory(path, coresRoot);
    }

    private static bool StartsWithDirectory(string path, string directory)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(directory))
        {
            return false;
        }

        string prefix = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetPath(Process process)
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
}
