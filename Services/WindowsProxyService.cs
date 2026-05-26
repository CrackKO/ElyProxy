using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ElyProxy.Services;

public class WindowsProxyService
{
    private const string InternetSettingsPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string AutoConfigUrlValue = "AutoConfigURL";
    private const int InternetOptionSettingsChanged = 39;
    private const int InternetOptionRefresh = 37;

    public string? GetAutoConfigUrl()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, false);
        return key?.GetValue(AutoConfigUrlValue) as string;
    }

    public bool IsPacEnabled(string pacUrl)
    {
        return string.Equals(GetAutoConfigUrl(), pacUrl, StringComparison.OrdinalIgnoreCase);
    }

    public void EnablePac(string pacUrl)
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, true)
            ?? Registry.CurrentUser.CreateSubKey(InternetSettingsPath, true);

        if (key == null)
            throw new InvalidOperationException("Не удалось открыть настройки прокси Windows.");

        key.SetValue(AutoConfigUrlValue, pacUrl, RegistryValueKind.String);
        NotifySettingsChanged();
    }

    public void DisablePac(string pacUrl, string? restoreAutoConfigUrl)
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, true)
            ?? Registry.CurrentUser.CreateSubKey(InternetSettingsPath, true);

        if (key == null)
            throw new InvalidOperationException("Не удалось открыть настройки прокси Windows.");

        var current = key.GetValue(AutoConfigUrlValue) as string;
        if (!string.Equals(current, pacUrl, StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrWhiteSpace(restoreAutoConfigUrl))
            key.DeleteValue(AutoConfigUrlValue, false);
        else
            key.SetValue(AutoConfigUrlValue, restoreAutoConfigUrl, RegistryValueKind.String);

        NotifySettingsChanged();
    }

    private static void NotifySettingsChanged()
    {
        InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
    }

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
}
