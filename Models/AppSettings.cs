namespace ElyProxy.Models;

public class AppSettings
{
    public int SocksPort { get; set; } = 1080;
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoStartWithWindows { get; set; }
    public bool AutoConnect { get; set; }
    public bool AutoReconnect { get; set; }
    public bool ShowLogs { get; set; } = true;
    public bool SystemProxyEnabled { get; set; }
    public List<string> SystemProxyBypassRules { get; set; } = new();
    public bool PacModeEnabled { get; set; }
    public int PacPort { get; set; } = 18080;
    public string? PreviousAutoConfigUrl { get; set; }
    public int SubscriptionUpdateIntervalMinutes { get; set; }
    public string? LastConnectedNodeId { get; set; }
    public List<VlessNode> ManualNodes { get; set; } = new();
}
