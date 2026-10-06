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

    void RestoreManualProxySettings(WindowsProxySnapshot snapshot);

    void RestorePacSettings(WindowsProxySnapshot snapshot);
}
