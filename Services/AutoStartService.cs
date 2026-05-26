using System.Diagnostics;
using Microsoft.Win32;

namespace ElyProxy.Services;

public class AutoStartService
{
    private const string AppName = "ElyProxy";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        var value = key?.GetValue(AppName) as string;
        var exePath = GetExecutablePath();

        return !string.IsNullOrWhiteSpace(value)
            && !string.IsNullOrWhiteSpace(exePath)
            && value.Contains(exePath, StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);

        if (key == null)
            throw new InvalidOperationException("Не удалось открыть раздел автозапуска Windows.");

        if (enabled)
        {
            key.SetValue(AppName, $"\"{GetExecutablePath()}\"");
            return;
        }

        key.DeleteValue(AppName, false);
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Не удалось определить путь к ElyProxy.exe.");
    }
}
