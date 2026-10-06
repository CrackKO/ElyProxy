using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ElyProxy.Models;

namespace ElyProxy.Services;

public sealed class RunningApplicationService
{
    public IReadOnlyList<RunningApplication> GetVisibleApplications(IEnumerable<string>? excludedPaths = null)
    {
        HashSet<string> excluded = new HashSet<string>(
            (excludedPaths ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizePath),
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> processCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    string? path = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        continue;
                    }

                    string normalizedPath = NormalizePath(path);
                    if (excluded.Contains(normalizedPath))
                    {
                        continue;
                    }

                    processCounts[normalizedPath] = processCounts.TryGetValue(normalizedPath, out int count)
                        ? count + 1
                        : 1;
                }
                catch
                {
                    // A process can exit or deny access while the list is being collected.
                }
            }
        }

        return processCounts
            .Select(pair => RunningApplication.FromPath(pair.Key, pair.Value))
            .OrderBy(application => application.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static string NormalizePath(string path)
    {
        string normalized = path.Trim().Trim('"');
        try
        {
            return Path.GetFullPath(normalized);
        }
        catch
        {
            return normalized;
        }
    }
}
