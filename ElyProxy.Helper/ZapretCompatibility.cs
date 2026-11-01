using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ElyProxy.Helper;

internal static class ZapretCompatibility
{
    private const string ServiceKey = @"SYSTEM\CurrentControlSet\Services\zapret";
    private const string OriginalValue = "ElyProxyOriginalImagePath";
    private const string AppliedValue = "ElyProxyCompatibleImagePath";

    // Only this named service can be changed. This operation is deliberately
    // a separate elevated CLI, never an arbitrary command on the helper pipe.
    public static int Run(bool restore)
    {
        using var operationLock = new Mutex(false, @"Global\ElyProxyZapretCompatibility");
        bool ownsLock;
        try { ownsLock = operationLock.WaitOne(0); }
        catch (AbandonedMutexException) { ownsLock = true; }
        if (!ownsLock) return 6;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ServiceKey, writable: true);
            if (key == null) return 2;
            string current = Read(key, "ImagePath");
            string original = Read(key, OriginalValue);
            string applied = Read(key, AppliedValue);
            if (!CanChange(current, original, applied)) return 4;
            if (restore && original.Length == 0) return 0;
            string baseline = original.Length == 0 ? current : original;
            string target;
            if (restore)
            {
                target = baseline;
            }
            else
            {
                int[] indices = GetPhysicalInterfaceIndices();
                target = BuildImagePath(baseline, indices, ReadAndValidateFilter(baseline));
                _ = ReadAndValidateFilter(target);
            }
            if (target == current) return 0;

            using var service = new ServiceController("zapret");
            service.Refresh();
            if (service.Status != ServiceControllerStatus.Running && service.Status != ServiceControllerStatus.Stopped)
                throw new InvalidOperationException("Дождитесь завершения запуска или остановки Zapret.");
            bool wasRunning = service.Status == ServiceControllerStatus.Running;
            try
            {
                // Save before touching SCM so even an interrupted apply is recoverable.
                key.SetValue(OriginalValue, baseline);
                key.SetValue(AppliedValue, target);
                key.Flush();
                if (wasRunning) Stop(service);
                ChangeImagePath(target);
                if (wasRunning) Start(service);
                if (restore)
                {
                    key.DeleteValue(OriginalValue, false);
                    key.DeleteValue(AppliedValue, false);
                }
                return 0;
            }
            catch
            {
                try
                {
                    service.Refresh();
                    if (service.Status != ServiceControllerStatus.Stopped) Stop(service);
                    ChangeImagePath(current);
                    if (wasRunning) Start(service);
                    RestoreValue(key, OriginalValue, original);
                    RestoreValue(key, AppliedValue, applied);
                }
                catch (Exception rollbackError)
                {
                    ReportError(rollbackError);
                    return 5;
                }
                throw;
            }
        }
        catch (NotSupportedException ex)
        {
            ReportError(ex);
            return 3;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            return 1;
        }
        finally { operationLock.ReleaseMutex(); }
    }

    private static void ReportError(Exception error)
    {
        Console.Error.WriteLine(error.Message);
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ElyProxy");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "zapret-compatibility.log"), DateTimeOffset.Now + " " + error + Environment.NewLine);
        }
        catch { /* Logging must never prevent rollback or change the result. */ }
    }

    internal static bool CanChange(string current, string original, string applied) =>
        (original.Length == 0 && applied.Length == 0) ||
        (original.Length > 0 && (current == original || (applied.Length > 0 && current == applied)));

    internal static string BuildImagePath(string original, IEnumerable<int> interfaceIndices, string captureFilter)
    {
        _ = ParseCommand(original);
        // --wf-raw replaces all partial filters, so appending one would silently do nothing.
        if (Regex.IsMatch(original, @"(?:^|\s)--wf-(?:raw|save)(?:=|\s|$)", RegexOptions.IgnoreCase))
            throw new NotSupportedException("Zapret с --wf-raw/--wf-save требует ручной настройки.");
        int[] indices = interfaceIndices.Distinct().Order().ToArray();
        if (indices.Length == 0 || indices.Any(index => index <= 0))
            throw new NotSupportedException("Не найдены физические сетевые адаптеры.");
        string interfaces = string.Join(" or ", indices.Select(index => "ifIdx == " + index));
        // WFP can expose the physical interface index even for traffic routed
        // into Wintun. Exclude inner tunnel addresses in both directions too.
        // These are the shared native Xray/sing-box/Hysteria tunnel addresses.
        if (string.IsNullOrWhiteSpace(captureFilter) || captureFilter.Contains('"'))
            throw new NotSupportedException("Некорректный исходный фильтр Zapret.");
        string filter = "(" + captureFilter.Trim().Replace('\r', ' ').Replace('\n', ' ') + ") and (" + interfaces + ")" +
            " and (not ip or (ip.SrcAddr != 172.19.0.1 and ip.DstAddr != 172.19.0.1))" +
            " and (not ipv6 or (ipv6.SrcAddr != fdfe:dcba:9876::1 and ipv6.DstAddr != fdfe:dcba:9876::1))";
        // Partial filters are OR-ed with port filters by winws. A full filter
        // is required to AND the exclusions with *all* existing capture rules.
        string target = original.TrimEnd() + " --wf-raw=\"(" + filter + ")\"";
        if (target.Length > 30000) throw new NotSupportedException("Слишком длинная командная строка Zapret.");
        return target;
    }

    internal static (string Executable, string Arguments) ParseCommand(string imagePath)
    {
        var match = Regex.Match(imagePath, "^\\s*(?:\"(?<exe>[^\"]+)\"|(?<exe>\\S+))(?<args>.*)$");
        string executable = Environment.ExpandEnvironmentVariables(match.Groups["exe"].Value);
        if (!match.Success || !Path.IsPathFullyQualified(executable) ||
            !string.Equals(Path.GetFileName(executable), "winws.exe", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Поддерживается служба zapret, запускающая winws.exe напрямую.");
        return (executable, match.Groups["args"].Value);
    }

    private static int[] GetPhysicalInterfaceIndices()
    {
        // Hardware classification comes from Windows, not adapter names (which can be renamed).
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var info = new ProcessStartInfo(Path.Combine(system, @"WindowsPowerShell\v1.0\powershell.exe"));
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add("$ErrorActionPreference='Stop'; Get-NetAdapter -Physical | ForEach-Object { $_.ifIndex }");
        string output = RunProcess(info);
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => int.Parse(line.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    }

    private static string ReadAndValidateFilter(string imagePath)
    {
        var (executable, arguments) = ParseCommand(imagePath);
        if (!File.Exists(executable)) throw new FileNotFoundException("winws.exe не найден.");
        string directory = Path.Combine(Path.GetTempPath(), "ElyProxy-Zapret-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string filterPath = Path.Combine(directory, "filter.txt");
            // winws parses the actual strategy and exits before opening WinDivert.
            var info = new ProcessStartInfo(executable)
            {
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                Arguments = arguments + " --wf-save=\"" + filterPath + "\""
            };
            try { _ = RunProcess(info); }
            catch (Exception ex) { throw new NotSupportedException("Эта конфигурация Zapret не прошла проверку фильтра.", ex); }
            if (!File.Exists(filterPath) || string.IsNullOrWhiteSpace(File.ReadAllText(filterPath)))
                throw new NotSupportedException("Версия Zapret не поддерживает фильтр совместимости.");
            string filter = File.ReadAllText(filterPath);
            CompileFilter(Path.Combine(Path.GetDirectoryName(executable)!, "WinDivert.dll"), filter);
            return filter;
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void CompileFilter(string libraryPath, string filter)
    {
        // --wf-save only builds the text. Compile it with the installed WinDivert
        // before restarting anything; older versions reject some filter syntax.
        IntPtr library = NativeLibrary.Load(libraryPath);
        try
        {
            var compile = Marshal.GetDelegateForFunctionPointer<CompileFilterDelegate>(NativeLibrary.GetExport(library, "WinDivertHelperCompileFilter"));
            if (!compile(filter, 0, IntPtr.Zero, 0, out IntPtr error, out uint position))
                throw new NotSupportedException($"WinDivert отклонил фильтр ({position}): {Marshal.PtrToStringAnsi(error)}");
        }
        finally { NativeLibrary.Free(library); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool CompileFilterDelegate([MarshalAs(UnmanagedType.LPStr)] string filter, int layer,
        IntPtr compiled, uint length, out IntPtr error, out uint position);

    private static string RunProcess(ProcessStartInfo info)
    {
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Не удалось запустить проверку.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new System.TimeoutException("Проверка Zapret превысила время ожидания.");
        }
        Task.WhenAll(output, error).GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new InvalidOperationException("Проверка завершилась с ошибкой: " + error.Result);
        return output.Result;
    }

    private static string Read(RegistryKey key, string name) => key.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
    private static void RestoreValue(RegistryKey key, string name, string value)
    {
        if (value.Length == 0) key.DeleteValue(name, false);
        else key.SetValue(name, value);
    }
    private static void Stop(ServiceController service)
    {
        service.Refresh();
        if (service.Status == ServiceControllerStatus.Stopped) return;
        if (service.Status != ServiceControllerStatus.StopPending) service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
    }
    private static void Start(ServiceController service)
    {
        service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
        // Catch configurations which report Running and immediately crash.
        Thread.Sleep(1000);
        service.Refresh();
        if (service.Status != ServiceControllerStatus.Running) throw new InvalidOperationException("Zapret остановился после запуска.");
    }

    private static void ChangeImagePath(string imagePath)
    {
        IntPtr manager = OpenSCManager(null, null, 1);
        if (manager == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            IntPtr service = OpenService(manager, "zapret", 2);
            if (service == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (!ChangeServiceConfig(service, uint.MaxValue, uint.MaxValue, uint.MaxValue,
                    imagePath, null, IntPtr.Zero, null, null, null, null))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig(IntPtr service, uint type, uint start, uint error,
        string binaryPath, string? group, IntPtr tag, string? dependencies, string? account, string? password, string? displayName);
    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
