namespace ElyProxy.Services;

public interface IWindowsProxyService
{
    string? GetAutoConfigUrl();

    WindowsProxySnapshot CaptureCurrentSettings();

    bool IsOwnedByPac(WindowsProxySnapshot current, IEnumerable<string> ownedPacUrls);

    bool IsOwnedByManualProxy(WindowsProxySnapshot current, int port, IEnumerable<string> bypassRules);

    void EnablePac(string pacUrl);

    void DisablePac(string pacUrl, string? restoreAutoConfigUrl);

    void EnableManualProxy(int port, IEnumerable<string> bypassRules);

    void EnableManualProxyPorts(int socksPort, int httpPort, IEnumerable<string> bypassRules)
    {
        if (socksPort == httpPort) { EnableManualProxy(socksPort, bypassRules); return; }
        var current = CaptureCurrentSettings();
        RestoreManualProxySettings(current with
        {
            ProxyEnable = new() { Kind = Microsoft.Win32.RegistryValueKind.DWord, DwordValue = 1 },
            ProxyServer = new() { Kind = Microsoft.Win32.RegistryValueKind.String, StringValue = $"http=127.0.0.1:{httpPort};https=127.0.0.1:{httpPort};socks=127.0.0.1:{socksPort}" },
            ProxyOverride = new() { Kind = Microsoft.Win32.RegistryValueKind.String, StringValue = string.Join(';', bypassRules.Where(r => !r.Contains('/')).Append("<local>").Distinct(StringComparer.OrdinalIgnoreCase)) },
            AutoConfigUrl = null
        });
    }

    void RestoreManualProxySettings(WindowsProxySnapshot snapshot);

    void RestorePacSettings(WindowsProxySnapshot snapshot);
}
