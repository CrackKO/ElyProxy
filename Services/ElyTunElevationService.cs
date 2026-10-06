using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;

namespace ElyProxy.Services;

public static class ElyTunElevationService
{
    public const string EnableElyTunArgument = "--enable-elytun";
    public const string ParentProcessArgumentPrefix = "--elytun-parent=";

    public static bool IsRunningAsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static Process StartElevatedForElyTun()
    {
        string executablePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Не удалось определить путь к ElyProxy.");

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true,
            Verb = "runas"
        };

        string executableName = Path.GetFileNameWithoutExtension(executablePath);
        if (string.Equals(executableName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            string assemblyPath = Assembly.GetEntryAssembly()?.Location
                ?? throw new InvalidOperationException("Не удалось определить сборку ElyProxy для повторного запуска.");
            startInfo.ArgumentList.Add(assemblyPath);
        }

        startInfo.ArgumentList.Add(EnableElyTunArgument);
        startInfo.ArgumentList.Add(ParentProcessArgumentPrefix + Environment.ProcessId);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows не смогла запустить ElyProxy от имени администратора.");
    }

    public static bool TryGetLaunchRequest(string[] arguments, out int? parentProcessId)
    {
        parentProcessId = null;
        bool enableElyTun = false;

        foreach (string argument in arguments)
        {
            if (string.Equals(argument, EnableElyTunArgument, StringComparison.OrdinalIgnoreCase))
            {
                enableElyTun = true;
            }
            else if (argument.StartsWith(ParentProcessArgumentPrefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(argument.Substring(ParentProcessArgumentPrefix.Length), out int processId)
                && processId > 0)
            {
                parentProcessId = processId;
            }
        }

        return enableElyTun;
    }

    public static async Task<bool> WaitForParentExitAsync(int? parentProcessId, TimeSpan timeout)
    {
        if (!parentProcessId.HasValue || parentProcessId.Value == Environment.ProcessId)
        {
            return true;
        }

        try
        {
            using Process parent = Process.GetProcessById(parentProcessId.Value);
            await parent.WaitForExitAsync().WaitAsync(timeout);
            return true;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (Win32Exception)
        {
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
